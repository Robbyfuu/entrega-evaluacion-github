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

    // ===== Multi-evaluacion: cursos, secciones, evaluaciones =====
    // Fallback: si la BD no responde, el caller cae a Config.Sections/EvaluationTypes.

    public async Task<List<Course>> GetCoursesAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(
                Rest("courses?active=eq.true&select=*&order=code.asc"));
            return JsonSerializer.Deserialize<List<Course>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<SectionRow>> GetSectionsAsync(long? courseId = null)
    {
        try
        {
            // PostgREST espera el filtro DESPUES del nombre de tabla:
            //   sections?course_id=eq.123&select=*
            // Antes se generaba "course_id=eq.123&sections?select=*" (URL invalida).
            var filter = courseId is { } cid ? $"?course_id=eq.{cid}&select=*&order=code.asc"
                                             : "?select=*&order=code.asc";
            var json = await _http.GetStringAsync(Rest($"sections{filter}"));
            return JsonSerializer.Deserialize<List<SectionRow>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<Evaluation>> GetEvaluationsAsync(long sectionId, bool onlyActive = true)
    {
        try
        {
            var activeFilter = onlyActive ? "&active=eq.true" : "";
            var json = await _http.GetStringAsync(
                Rest($"evaluations?section_id=eq.{sectionId}{activeFilter}&select=*&order=created_at.desc"));
            return JsonSerializer.Deserialize<List<Evaluation>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    // ===== Assignments =====
    /// <summary>
    /// Trae assignments activos. Si se pasa evaluationId, filtra por
    /// evaluation_id=eq.X (cuando el alumno ya eligio una evaluacion
    /// concreta). Sin filtro, trae todos los activos (comportamiento
    /// legacy para coexistencia con clientes viejos).
    /// </summary>
    public async Task<List<Assignment>> GetActiveAssignmentsAsync(long? evaluationId = null)
    {
        try
        {
            var evalFilter = evaluationId is { } id ? $"evaluation_id=eq.{id}&" : "";
            var json = await _http.GetStringAsync(
                Rest($"assignments?{evalFilter}active=eq.true&select=*&order=created_at.desc"));
            return JsonSerializer.Deserialize<List<Assignment>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    // ===== Blocklist de procesos sospechosos =====

    /// <summary>
    /// Devuelve la lista efectiva de procesos sospechosos para una seccion:
    /// reglas globales (section IS NULL) union reglas de la seccion dada.
    /// Soporta tanto section TEXT (legacy) como section_id (multi-evaluacion).
    /// Si sectionId viene != null, filtra por section_id; si no, cae a section
    /// TEXT (forward-compat con clientes viejos). Cada process_name se
    /// normaliza (paridad con la deteccion) y se arma un HashSet.
    ///
    /// Devuelve null en CUALQUIER error de red/parseo => el caller cae al
    /// fallback (Config.SuspiciousProcesses). NO devuelve set vacio en error:
    /// null y [] significan cosas distintas (null=fallo/usar fallback,
    /// []=tabla vacia que el detector tambien trata como fallback).
    /// </summary>
    public async Task<HashSet<string>?> GetBlocklistAsync(string? section, long? sectionId = null)
    {
        try
        {
            // section.is.null cubre las reglas globales; si hay seccion se suma
            // section.eq.X (o section_id.eq.Y cuando se usa multi-evaluacion).
            // PostgREST OR: or=(section.is.null,section.eq.X).
            // El valor va entre comillas dobles para que PostgREST lo trate
            // como literal. Se url-encodea igual.
            string filter;
            if (sectionId is { } sid)
            {
                // Multi-evaluacion: filtrar por section_id (preferido) ya que
                // section TEXT puede ser NULL en filas migradas.
                filter = $"or=(section.is.null,section_id.eq.{sid})";
            }
            else if (string.IsNullOrWhiteSpace(section))
            {
                filter = "section=is.null";
            }
            else
            {
                var quoted = Uri.EscapeDataString($"\"{section}\"");
                filter = $"or=(section.is.null,section.eq.{quoted})";
            }

            var json = await _http.GetStringAsync(
                Rest($"suspicious_processes?{filter}&select=process_name"));
            var rows = JsonSerializer.Deserialize<List<SuspiciousProcess>>(json, JsonOpts);
            if (rows == null) return null;

            var set = new HashSet<string>();
            foreach (var r in rows)
            {
                var norm = Config.NormalizeProcessName(r.ProcessName);
                if (norm.Length > 0) set.Add(norm);
            }
            return set;
        }
        catch { return null; }
    }

    // ===== Aceptaciones de tareas =====

    /// <summary>
    /// Registra (upsert) que un alumno acepto una tarea de Classroom via RPC
    /// SECURITY DEFINER. Mismo patron silencioso que SendHeartbeatAsync.
    /// </summary>
    public async Task RecordAcceptanceAsync(
        string githubUsername, long assignmentId, string? title,
        string? section, string? repoName, string? repoUrl,
        long? evaluationId = null)
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
                p_repo_url = repoUrl,
                p_evaluation_id = evaluationId
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

    /// <summary>
    /// Devuelve las entregas formales registradas de un alumno. Lista vacia si falla.
    /// </summary>
    public async Task<List<Submission>> GetSubmissionsAsync(string githubUsername)
    {
        try
        {
            var user = Uri.EscapeDataString(githubUsername);
            var json = await _http.GetStringAsync(
                Rest($"assignment_submissions?github_username=eq.{user}&select=*"));
            return JsonSerializer.Deserialize<List<Submission>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>
    /// Registra (upsert) que un alumno entrego un repo via RPC SECURITY DEFINER.
    /// </summary>
    public async Task RecordSubmissionAsync(long assignmentId, string githubUsername, string repoUrl)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_assignment_id = assignmentId,
                p_github_username = githubUsername,
                p_repo_url = repoUrl,
                p_status = "submitted"
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _http.PostAsync(Rest("rpc/record_submission"), content);
        }
        catch { }
    }

    // ===== Heartbeat (RPC SECURITY DEFINER) =====
    // section_id se sincroniza via trigger trg_sync_section_online desde
    // section TEXT; la RPC heartbeat no acepta p_section_id (forward-compat).
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
        string pcName, string? section, string? repoName, string? repoUrl,
        long? sectionId = null)
    {
        await PostInsertAsync("student_activity", new
        {
            action, github_username = githubUsername, github_email = githubEmail,
            pc_name = pcName, section, section_id = sectionId,
            repo_name = repoName, repo_url = repoUrl
        });
    }

    /// <summary>
    /// Reporta una alerta de proceso sospechoso via RPC SECURITY DEFINER
    /// (anti-flood server-side: rate-limit 30s por pc_name+process_name). El
    /// cliente ya no inserta directo en process_alerts. Patron y catch silencioso
    /// identicos a SendHeartbeatAsync. CRITICO: nombre y orden de los argumentos
    /// deben coincidir con la firma SQL report_process_alert(
    ///   p_github_username, p_pc_name, p_section, p_process_name, p_window_title).
    /// </summary>
    public async Task ReportProcessAlertAsync(
        string githubUsername, string pcName, string? section,
        string processName, string windowTitle)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                p_github_username = githubUsername,
                p_pc_name = pcName,
                p_section = section,
                p_process_name = processName,
                p_window_title = windowTitle
            }, JsonOpts);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(Rest("rpc/report_process_alert"), content);
            // La alerta es evidencia de proctoring: si la RPC responde error
            // (RLS/grant regresion, fallo del rate-limit, etc.) dejamos rastro
            // en Debug en vez de tragarlo del todo. Sigue siendo no-fatal para
            // el alumno (catch silencioso para la UI).
            if (!resp.IsSuccessStatusCode)
                System.Diagnostics.Debug.WriteLine(
                    $"[SupabaseClient] report_process_alert fallo: {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SupabaseClient] report_process_alert excepcion: {ex.Message}");
        }
    }

    /// <summary>
    /// Registra una navegacion del alumno en el navegador embebido. INSERT
    /// directo (RLS anon insert), silencioso. allowed=true cuando el dominio
    /// estaba en la whitelist; allowed=false cuando se bloqueo (trampa).
    /// </summary>
    public async Task ReportBrowsingAsync(
        string githubUsername, string pcName, string? section,
        string url, string domain, bool allowed,
        long? sectionId = null)
    {
        await PostInsertAsync("browser_history", new
        {
            github_username = githubUsername, pc_name = pcName, section,
            section_id = sectionId, url, domain, allowed
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
