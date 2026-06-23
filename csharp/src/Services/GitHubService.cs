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
    // DOS HttpClients para cubrir ambos escenarios sin tocar el registro del
    // sistema (los navegadores siguen bloqueados durante el bloqueo):
    //   _httpViaProxy: UseProxy=true  -> respeta proxy del sistema (Fortinet,
    //                  proxy del aula, captive portal). Se usa durante el login
    //                  cuando NO hay bloqueo activo. Replica el comportamiento
    //                  de Invoke-RestMethod de la version .ps1 que funcionaba.
    //   _httpDirect:   UseProxy=false -> ignora el proxy del sistema. Se usa
    //                  durante el bloqueo (ProxyServer=127.0.0.1:1 blackhole)
    //                  para que la entrega de evaluaciones llegue a GitHub.
    // La seleccion es por llamada via la property Http, no por sesion, asi que
    // si el bloqueo cambia a mitad de sesion, la siguiente llamada usa el
    // HttpClient correcto automaticamente.
    private readonly HttpClient _httpViaProxy;
    private readonly HttpClient _httpDirect;
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
        // HttpClient que respeta el proxy del sistema (Fortinet, proxy del aula,
        // captive portal). Se usa durante el login (sin bloqueo activo).
        var handlerViaProxy = new HttpClientHandler { UseProxy = true };
        _httpViaProxy = new HttpClient(handlerViaProxy) { Timeout = TimeSpan.FromSeconds(30) };

        // HttpClient que ignora el proxy del sistema. Se usa durante el bloqueo
        // (ProxyServer=127.0.0.1:1) para que la entrega llegue a GitHub sin
        // caer en el blackhole.
        var handlerDirect = new HttpClientHandler { UseProxy = false, Proxy = null };
        _httpDirect = new HttpClient(handlerDirect) { Timeout = TimeSpan.FromSeconds(30) };

        // 30s en vez de 15s: en redes de aula con filtrado/VPN/WiFi saturado el
        // handshake TLS + POST supera 15s y cada poll daba timeout.
        ConfigureHeaders(_httpViaProxy);
        ConfigureHeaders(_httpDirect);
        LoadToken();
    }

    private void ConfigureHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EntregaEvaluacion/2.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>
    /// Selecciona el HttpClient segun el estado actual del bloqueo. Se evalua
    /// en cada llamada, no en el constructor, para reaccionar a cambios de
    /// bloqueo durante la sesion (AdminTick puede activar/desactivar el bloqueo
    /// en cualquier momento).
    /// </summary>
    private HttpClient Http => InternetBlockService.IsBlocked() ? _httpDirect : _httpViaProxy;

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
        _httpViaProxy.DefaultRequestHeaders.Authorization = null;
        _httpDirect.DefaultRequestHeaders.Authorization = null;
        try { File.Delete(TokenFile); } catch { }
    }

    private void ApplyAuthHeader()
    {
        // Aplicar a AMBOS HttpClients: el token se carga al arrancar (antes de
        // saber si habra bloqueo) y debe estar disponible sin importar cual
        // HttpClient se seleccione despues.
        if (!string.IsNullOrEmpty(_token))
        {
            var auth = new AuthenticationHeaderValue("Bearer", _token);
            _httpViaProxy.DefaultRequestHeaders.Authorization = auth;
            _httpDirect.DefaultRequestHeaders.Authorization = auth;
        }
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
            var resp = await Http.SendAsync(req);
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
        using (var resp = await Http.SendAsync(req))
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
            var json = await Http.GetStringAsync("https://api.github.com/user");
            return JsonSerializer.Deserialize<GitHubUser>(json, JsonOpts);
        }
        catch { return null; }
    }

    public async Task<List<GitHubRepo>> ListReposAsync()
    {
        if (!IsAuthenticated) return new();
        try
        {
            var json = await Http.GetStringAsync(
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
            var json = await Http.GetStringAsync(
                $"https://api.github.com/repos/{owner}/{repo}");
            return JsonSerializer.Deserialize<GitHubRepo>(json, JsonOpts);
        }
        catch { return null; }
    }

    /// <summary>
    /// Lista las invitaciones de repo pendientes del alumno.
    ///
    /// Devuelve null en CUALQUIER error de red/parseo o sin sesion => el caller
    /// trata null como "no se pudo verificar invitaciones" y NUNCA como "0
    /// invitaciones". Mismo patron null-vs-vacio que SupabaseClient.GetBlocklistAsync:
    /// null y [] significan cosas distintas (null=fallo/desconocido, []=no hay
    /// invitaciones pendientes). Tragarse el error a lista vacia hacia que el
    /// banner mostrara "0 pendientes" cuando en realidad no se pudo consultar.
    /// </summary>
    public async Task<List<RepoInvitation>?> GetPendingInvitationsAsync()
    {
        if (!IsAuthenticated) return null;
        try
        {
            var json = await Http.GetStringAsync(
                "https://api.github.com/user/repository_invitations");
            return JsonSerializer.Deserialize<List<RepoInvitation>>(json, JsonOpts);
        }
        catch { return null; }
    }

    public async Task<bool> AcceptInvitationAsync(long invitationId)
    {
        if (!IsAuthenticated) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Patch,
                $"https://api.github.com/user/repository_invitations/{invitationId}");
            var resp = await Http.SendAsync(req);
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
            var resp = await Http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
