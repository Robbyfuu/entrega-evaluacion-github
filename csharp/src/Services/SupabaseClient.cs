using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Cliente REST de Supabase. Anon key safe-to-share; escrituras protegidas
/// por RLS y/o RPC SECURITY DEFINER.
/// </summary>
public class SupabaseClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SupabaseClient()
    {
        // CRITICO: UseProxy=false. Sin esto, cuando bloqueamos internet (proxy
        // 127.0.0.1:1), el propio cliente perderia conexion a Supabase y nunca
        // recibiria la orden de desbloquear (catch-22). Ignorar el proxy del
        // sistema garantiza comunicacion con el backend siempre.
        var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("apikey", Config.SupabaseAnonKey);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Config.SupabaseAnonKey);
    }

    private string Rest(string path) => $"{Config.SupabaseUrl}/rest/v1/{path}";

    // ===== Control =====
    public async Task<ControlState?> GetControlAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(Rest("control?id=eq.1&select=*"));
            var arr = JsonSerializer.Deserialize<ControlState[]>(json, JsonOpts);
            return arr is { Length: > 0 } ? arr[0] : null;
        }
        catch { return null; }
    }

    // ===== Assignments =====
    public async Task<List<Assignment>> GetActiveAssignmentsAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(
                Rest("assignments?active=eq.true&select=*&order=created_at.desc"));
            return JsonSerializer.Deserialize<List<Assignment>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    // ===== Aceptaciones de tareas =====

    /// <summary>
    /// Registra (upsert) que un alumno acepto una tarea de Classroom via RPC
    /// SECURITY DEFINER. Mismo patron silencioso que SendHeartbeatAsync.
    /// </summary>
    public async Task RecordAcceptanceAsync(
        string githubUsername, long assignmentId, string? title,
        string? section, string? repoName, string? repoUrl)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_github_username = githubUsername,
                p_assignment_id = assignmentId,
                p_assignment_title = title,
                p_section = section,
                p_repo_name = repoName,
                p_repo_url = repoUrl
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _http.PostAsync(Rest("rpc/record_acceptance"), content);
        }
        catch { }
    }

    /// <summary>
    /// Devuelve las aceptaciones registradas de un alumno. Lista vacia si falla.
    /// </summary>
    public async Task<List<Acceptance>> GetAcceptancesAsync(string githubUsername)
    {
        try
        {
            var user = Uri.EscapeDataString(githubUsername);
            var json = await _http.GetStringAsync(
                Rest($"assignment_acceptances?github_username=eq.{user}&select=*"));
            return JsonSerializer.Deserialize<List<Acceptance>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    // ===== Heartbeat (RPC SECURITY DEFINER) =====
    public async Task SendHeartbeatAsync(
        string pcName, string githubUsername, string? githubEmail,
        string? section, List<ProcessInfo> processes,
        string internetState = "free", string lockdownState = "none")
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_pc_name = pcName,
                p_github_username = githubUsername,
                p_github_email = githubEmail,
                p_section = section,
                p_processes = processes,
                p_internet_state = internetState,
                p_lockdown_state = lockdownState
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _http.PostAsync(Rest("rpc/heartbeat"), content);
        }
        catch { }
    }

    // ===== Targeted lockdown =====
    public async Task<bool> IsTargetedLockedAsync(string pcName, string githubUsername)
    {
        try
        {
            var pc = Uri.EscapeDataString(pcName);
            var user = Uri.EscapeDataString(githubUsername);
            var json = await _http.GetStringAsync(
                Rest($"targeted_lockdowns?pc_name=eq.{pc}&github_username=eq.{user}&active=eq.true&select=id,reason"));
            var arr = JsonSerializer.Deserialize<TargetedLockdown[]>(json, JsonOpts);
            return arr is { Length: > 0 };
        }
        catch { return false; }
    }

    public async Task<string?> GetTargetedReasonAsync(string pcName, string githubUsername)
    {
        try
        {
            var pc = Uri.EscapeDataString(pcName);
            var user = Uri.EscapeDataString(githubUsername);
            var json = await _http.GetStringAsync(
                Rest($"targeted_lockdowns?pc_name=eq.{pc}&github_username=eq.{user}&active=eq.true&select=reason"));
            var arr = JsonSerializer.Deserialize<TargetedLockdown[]>(json, JsonOpts);
            return arr is { Length: > 0 } ? arr[0].Reason : null;
        }
        catch { return null; }
    }

    public async Task<bool> IsForceLockdownAsync()
    {
        var ctl = await GetControlAsync();
        return ctl?.ForceLockdown ?? false;
    }

    // ===== Reportes (INSERT directo, RLS anon insert) =====
    public async Task ReportCheatEventAsync(
        string username, string pcName, string repoName, int filesCount, string[] filesSample)
    {
        await PostInsertAsync("cheat_events", new
        {
            username, pc_name = pcName, repo_name = repoName,
            files_count = filesCount, files_sample = filesSample
        });
    }

    public async Task ReportStudentActivityAsync(
        string action, string githubUsername, string? githubEmail,
        string pcName, string? section, string? repoName, string? repoUrl)
    {
        await PostInsertAsync("student_activity", new
        {
            action, github_username = githubUsername, github_email = githubEmail,
            pc_name = pcName, section, repo_name = repoName, repo_url = repoUrl
        });
    }

    public async Task ReportProcessAlertAsync(
        string githubUsername, string pcName, string? section,
        string processName, string windowTitle)
    {
        await PostInsertAsync("process_alerts", new
        {
            github_username = githubUsername, pc_name = pcName, section,
            process_name = processName, window_title = windowTitle
        });
    }

    /// <summary>
    /// Registra una navegacion del alumno en el navegador embebido. INSERT
    /// directo (RLS anon insert), silencioso. allowed=true cuando el dominio
    /// estaba en la whitelist; allowed=false cuando se bloqueo (trampa).
    /// </summary>
    public async Task ReportBrowsingAsync(
        string githubUsername, string pcName, string? section,
        string url, string domain, bool allowed)
    {
        await PostInsertAsync("browser_history", new
        {
            github_username = githubUsername, pc_name = pcName, section,
            url, domain, allowed
        });
    }

    private async Task PostInsertAsync(string table, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _http.PostAsync(Rest(table), content);
        }
        catch { }
    }
}
