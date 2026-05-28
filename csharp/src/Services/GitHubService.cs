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
        // UseProxy=false: ignorar el proxy del sistema (ver SupabaseClient).
        // GitHub API y device flow funcionan aunque el internet "bloqueado".
        var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("EntregaEvaluacion/2.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        LoadToken();
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
        var resp = await _http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<AccessTokenResponse>(json, JsonOpts);

        if (data?.AccessToken is { Length: > 0 } tok)
        {
            SaveToken(tok.Trim());
            return _token;
        }
        return data?.Error switch
        {
            "authorization_pending" => null,
            "slow_down" => null,
            "expired_token" => throw new TimeoutException("Codigo expirado"),
            "access_denied" => throw new UnauthorizedAccessException("Acceso denegado"),
            _ => null
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
