using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Autenticacion + API de GitHub nativa (sin gh CLI). Device flow OAuth +
/// llamadas a la API REST. El token se guarda en memoria y en disco cifrado.
/// </summary>
public class GitHubService
{
    private readonly HttpClient _http;
    private string? _token;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string TokenFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EntregaEvaluacion", "token.dat");

    public GitHubService()
    {
        // UseProxy=false FIJO: ignorar el proxy del sistema. CRITICO para que
        // la entrega de evaluaciones funcione durante el bloqueo: cuando el
        // profe activa el bloqueo, ProxyServer pasa a ser 127.0.0.1:1
        // (blackhole) y cualquier llamada que respete el proxy del sistema
        // caeria al blackhole. La app DEBE poder llegar a GitHub durante el
        // bloqueo para que el alumno entregue.
        //
        // Trade-off: en aulas con proxy obligatorio, este UseProxy=false impide
        // que el device flow llegue a GitHub durante el login (el navegador
        // embebido SI usa el proxy del sistema, por eso el alumno puede validar
        // el codigo aunque el polling falle). La solucion definitiva (v2.6.0)
        // es ProxyOverride en InternetBlockService con los dominios de GitHub
        // y Supabase exceptuados del blackhole, mas UseProxy=true siempre.
        // Ver memo en memoria: architecture/internet-block-proxyoverride.
        var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
        // 30s en vez de 15s: en redes de aula con filtrado/VPN/WiFi saturado el
        // handshake TLS + POST supera 15s y cada poll daba timeout, por lo que
        // la app nunca recibia el token. El WebView2 gestiona timeouts largos
        // por eso el alumno lograba validar el codigo aunque el polling fallara.
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("EntregaEvaluacion/2.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        LoadToken();
    }

    /// <summary>
    /// Lanzada cuando GitHub responde "slow_down" en el device flow. El caller
    /// (LoginWindow) debe aumentar el intervalo del timer en AddSeconds segun
    /// la spec de OAuth 2.0 device flow (rfc 8628 sec 3.5).
    /// </summary>
    public class SlowDownException : Exception
    {
        public int AddSeconds { get; }
        public SlowDownException(int add) : base($"GitHub pidio ir mas lento (+{add}s)")
            => AddSeconds = add;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
    public string? Token => _token;

    // ===== Persistencia del token (DPAPI - cifrado por usuario) =====
    private void LoadToken()
    {
        try
        {
            if (File.Exists(TokenFile))
            {
                var enc = File.ReadAllBytes(TokenFile);
                var raw = System.Security.Cryptography.ProtectedData.Unprotect(
                    enc, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                _token = Encoding.UTF8.GetString(raw);
                ApplyAuthHeader();
            }
        }
        catch { _token = null; }
    }

    private void SaveToken(string token)
    {
        _token = token;
        ApplyAuthHeader();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
            var raw = Encoding.UTF8.GetBytes(token);
            var enc = System.Security.Cryptography.ProtectedData.Protect(
                raw, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFile, enc);
        }
        catch { }
    }

    public void Logout()
    {
        _token = null;
        _http.DefaultRequestHeaders.Authorization = null;
        try { File.Delete(TokenFile); } catch { }
    }

    private void ApplyAuthHeader()
    {
        if (!string.IsNullOrEmpty(_token))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
    }

    // ===== Device flow =====
    public async Task<DeviceCodeResponse?> RequestDeviceCodeAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://github.com/login/device/code");
            req.Headers.Accept.Clear();
            req.Headers.Accept.ParseAdd("application/json");
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Config.GitHubClientId,
                ["scope"] = Config.GitHubScopes
            });
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<DeviceCodeResponse>(json, JsonOpts);
        }
        catch { return null; }
    }

    /// <summary>
    /// Poll una vez el endpoint de token. Devuelve:
    /// - token si autorizado
    /// - null si pending/slow_down (seguir esperando)
    /// - lanza si expired/denied
    ///
    /// Tipos de excepcion deliberados para que LoginWindow distinga fatal vs
    /// transitorio y deje de tragarse errores en silencio:
    ///   TimeoutException             -> codigo de GitHub expirado (fatal)
    ///   UnauthorizedAccessException  -> acceso denegado (fatal)
    ///   TaskCanceledException        -> timeout de red del HttpClient (transitorio)
    ///   HttpRequestException         -> sin ruta a GitHub (transitorio)
    ///   InvalidOperationException    -> JSON inesperado o error GitHub desconocido
    /// </summary>
    public async Task<string?> PollAccessTokenAsync(string deviceCode)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://github.com/login/oauth/access_token");
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd("application/json");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = Config.GitHubClientId,
            ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
        });

        // SendAsync lanza HttpRequestException (sin ruta: DNS / proxy del aula
        // / firewall de Windows negando la app / cable) y TaskCanceledException
        // (timeout del HttpClient de 15s, tipico de red lenta). Se propagan
        // tal cual: LoginWindow distingue por tipo.
        string json;
        System.Net.HttpStatusCode status;
        using (var resp = await _http.SendAsync(req))
        {
            status = resp.StatusCode;
            json = await resp.Content.ReadAsStringAsync();
        }

        AccessTokenResponse? data;
        try
        {
            data = JsonSerializer.Deserialize<AccessTokenResponse>(json, JsonOpts);
        }
        catch (JsonException)
        {
            // El cuerpo no es JSON valido: lo mas probable es un captive
            // portal, un AV haciendo MITM del TLS, o una pagina HTML del proxy
            // del aula interponiendose. Mostramos preview para diagnostico.
            var preview = json.Length > 150 ? json[..150] + "..." : json;
            throw new InvalidOperationException(
                $"GitHub devolvio algo que no es JSON (¿captive portal / AV?). Preview: {preview}");
        }

        if (data?.AccessToken is { Length: > 0 } tok)
        {
            SaveToken(tok.Trim());
            return _token;
        }
        return data?.Error switch
        {
            "authorization_pending" => null,
            "slow_down" => throw new SlowDownException(5),
            "expired_token" => throw new TimeoutException("Codigo de GitHub expirado."),
            "access_denied" => throw new UnauthorizedAccessException("Acceso denegado por el alumno."),
            var unknown => throw new InvalidOperationException(
                $"GitHub devolvio error desconocido '{unknown}'. Status={status}, Body={json}")
        };
    }

    // ===== API =====
    public async Task<GitHubUser?> GetUserAsync()
    {
        if (!IsAuthenticated) return null;
        try
        {
            var json = await _http.GetStringAsync("https://api.github.com/user");
            return JsonSerializer.Deserialize<GitHubUser>(json, JsonOpts);
        }
        catch { return null; }
    }

    public async Task<List<GitHubRepo>> ListReposAsync()
    {
        if (!IsAuthenticated) return new();
        try
        {
            var json = await _http.GetStringAsync(
                "https://api.github.com/user/repos?per_page=100&sort=updated");
            return JsonSerializer.Deserialize<List<GitHubRepo>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    public async Task<GitHubRepo?> GetRepoAsync(string owner, string repo)
    {
        if (!IsAuthenticated) return null;
        try
        {
            var json = await _http.GetStringAsync(
                $"https://api.github.com/repos/{owner}/{repo}");
            return JsonSerializer.Deserialize<GitHubRepo>(json, JsonOpts);
        }
        catch { return null; }
    }

    public async Task<List<RepoInvitation>> GetPendingInvitationsAsync()
    {
        if (!IsAuthenticated) return new();
        try
        {
            var json = await _http.GetStringAsync(
                "https://api.github.com/user/repository_invitations");
            return JsonSerializer.Deserialize<List<RepoInvitation>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    public async Task<bool> AcceptInvitationAsync(long invitationId)
    {
        if (!IsAuthenticated) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Patch,
                $"https://api.github.com/user/repository_invitations/{invitationId}");
            var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> CreateRepoAsync(string name, bool isPublic)
    {
        if (!IsAuthenticated) return false;
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                name,
                @private = !isPublic,
                auto_init = false
            });
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://api.github.com/user/repos")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
