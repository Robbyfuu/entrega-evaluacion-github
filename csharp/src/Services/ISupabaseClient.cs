using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Espejo 1:1 de la superficie publica de instancia de SupabaseClient: cliente
/// REST/RPC de Supabase (control, lockdown, roster, heartbeat, reportes e
/// identidad verificada). Interfaz transitoria (sin inyeccion todavia) para que
/// los consumidores dependan de la abstraccion en lugar de la clase concreta;
/// la construccion sigue siendo new SupabaseClient(). Es deliberadamente una
/// sola interfaz "gruesa": la division en repositorios es un paso posterior.
/// Los miembros estaticos/privados y el tipo anidado de respuesta NO forman
/// parte del contrato.
/// </summary>
public interface ISupabaseClient
{
    // ===== Identidad verificada =====
    // Registra el proveedor del token de GitHub para re-enrolar la identidad.
    void SetGitHubTokenProvider(Func<string?>? provider);
    // Porta (o limpia con null) el JWT de identidad verificada.
    void SetIdentityToken(string? token);
    // Intercambia el token de GitHub por un JWT de identidad firmado.
    Task<bool> EnrollIdentityAsync(string githubToken);
    // Re-enrola la identidad si el JWT vigente esta por expirar (best-effort).
    Task EnsureIdentityFreshAsync();

    // ===== Control / lockdown =====
    // Override por PC (desbloqueo de internet/pantalla por nombre de equipo).
    Task<PcOverride?> GetPcOverrideAsync(string pcName);
    // True solo si el profe libero la pantalla de ESTE PC (fail-safe CERRADO).
    Task<bool> IsPcScreenUnblockedAsync(string pcName);
    // Control global id=1.
    Task<ControlState?> GetControlAsync();
    // Override de control por evaluacion (ok=false distingue fetch fallido).
    Task<(bool ok, EvaluationControl? row)> GetEvaluationControlAsync(long evaluationId);
    // Control EFECTIVO: override por evaluacion ?? global, resuelto campo a campo.
    Task<ControlState?> GetEffectiveControlAsync(long? evaluationId);

    // ===== Multi-evaluacion: cursos, secciones, evaluaciones =====
    // Cursos activos.
    Task<List<Course>> GetCoursesAsync();
    // Secciones (opcionalmente filtradas por curso).
    Task<List<SectionRow>> GetSectionsAsync(long? courseId = null);
    // Evaluaciones de una seccion.
    Task<List<Evaluation>> GetEvaluationsAsync(long sectionId, bool onlyActive = true);

    // ===== Assignments =====
    // Assignments activos (opcionalmente filtrados por evaluacion).
    Task<List<Assignment>> GetActiveAssignmentsAsync(long? evaluationId = null);

    // ===== Listas (proceso/url) =====
    // Blocklist efectiva de procesos (null en error => fallback de Config).
    Task<HashSet<string>?> GetBlocklistAsync(string? section, long? sectionId = null);
    // Allowlist efectiva de URLs (null en error o vacio => fallback de Config).
    Task<List<AllowedUrl>?> GetAllowlistAsync(string? section, long? sectionId = null);

    // ===== Aceptaciones y entregas =====
    // Registra (upsert) que un alumno acepto una tarea de Classroom.
    Task RecordAcceptanceAsync(
        string githubUsername, long assignmentId, string? title,
        string? section, string? repoName, string? repoUrl,
        long? evaluationId = null);
    // Aceptaciones registradas de un alumno.
    Task<List<Acceptance>> GetAcceptancesAsync(string githubUsername);
    // Entregas formales registradas de un alumno.
    Task<List<Submission>> GetSubmissionsAsync(string githubUsername);
    // Registra (upsert) que un alumno entrego un repo.
    Task RecordSubmissionAsync(long assignmentId, string githubUsername, string repoUrl);

    // ===== Roster =====
    // Confirma la matricula del alumno contra el roster (RPC no-PII).
    Task<MyEnrollment?> GetMyEnrollmentAsync(string githubUsername, long sectionId);

    // ===== Tiempo del examen (countdown anti-tamper) =====
    // Hora autoritativa del servidor + fin del examen (RPC get_exam_time).
    // null si no hay evaluacion, si la RPC no esta desplegada o ante cualquier
    // error (degradar: sin countdown). El parseo a DateTimeOffset lo hace el caller.
    Task<ExamTime?> GetExamTimeAsync(long? evaluationId);

    // ===== Heartbeat =====
    // Reporta presencia + estado del alumno (RPC).
    Task SendHeartbeatAsync(
        string pcName, string githubUsername, string? githubEmail,
        string? section, List<ProcessInfo> processes,
        string internetState = "free", string lockdownState = "none",
        long? evaluationId = null, string? appVersion = null);

    // ===== Targeted lockdown =====
    // True si hay lockdown dirigido activo para este PC+usuario (fail-safe CERRADO).
    Task<bool> IsTargetedLockedAsync(string pcName, string githubUsername);
    // Motivo del lockdown dirigido activo (null si no hay).
    Task<string?> GetTargetedReasonAsync(string pcName, string githubUsername);
    // Reporta que este PC+usuario quedo bloqueado por una trampa local.
    Task<bool> ReportSelfLockAsync(
        string pcName, string githubUsername, string? section, string? reason);
    // Predicado de force_lockdown EFECTIVO de la evaluacion indicada (override
    // por evaluacion ?? global). El llamador provee el evaluation_id; el cliente
    // ya no lo lee de un estado global.
    Task<bool> IsForceLockdownAsync(long? evaluationId);

    // ===== Reportes (INSERT directo / RPC) =====
    // Reporta un evento de copia detectado.
    Task ReportCheatEventAsync(
        string username, string pcName, string repoName, int filesCount, string[] filesSample);
    // Reporta una accion del alumno (login, clon, entrega, etc.).
    Task ReportStudentActivityAsync(
        string action, string githubUsername, string? githubEmail,
        string pcName, string? section, string? repoName, string? repoUrl,
        long? sectionId = null);
    // Reporta una alerta de proceso sospechoso (RPC con rate-limit server-side).
    Task ReportProcessAlertAsync(
        string githubUsername, string pcName, string? section,
        string processName, string windowTitle);
    // Registra una navegacion del alumno en el navegador embebido.
    Task ReportBrowsingAsync(
        string githubUsername, string pcName, string? section,
        string url, string domain, bool allowed,
        long? sectionId = null);
}
