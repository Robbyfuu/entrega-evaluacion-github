using System.Text.Json.Serialization;

namespace EntregaEvaluacion.Models;

public class ControlState
{
    [JsonPropertyName("internet_block")] public bool InternetBlock { get; set; }
    [JsonPropertyName("force_lockdown")] public bool ForceLockdown { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("updated_by")] public string? UpdatedBy { get; set; }
}

// ===== Multi-evaluacion: curso > seccion > evaluacion =====
// Fetcheados de Supabase al arrancar; fallback a Config.cs si la BD no responde.

public class Course
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("active")] public bool Active { get; set; }
}

public class SectionRow
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("course_id")] public long CourseId { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class Evaluation
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("section_id")] public long SectionId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("classroom_url")] public string? ClassroomUrl { get; set; }
    [JsonPropertyName("org")] public string? Org { get; set; }
    [JsonPropertyName("active")] public bool Active { get; set; }
}

public class Assignment
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("classroom_url")] public string? ClassroomUrl { get; set; } = "";
    [JsonPropertyName("section")] public string? Section { get; set; }
    [JsonPropertyName("org")] public string? Org { get; set; }
    [JsonPropertyName("active")] public bool Active { get; set; }
    [JsonPropertyName("evaluation_id")] public long? EvaluationId { get; set; }
    [JsonPropertyName("allows_manual_submission")] public bool AllowsManualSubmission { get; set; }
}

// Registro de aceptacion de una tarea de Classroom por parte de un alumno.
// Se persiste via RPC record_acceptance y se lee de assignment_acceptances.
public class Acceptance
{
    [JsonPropertyName("github_username")] public string GithubUsername { get; set; } = "";
    [JsonPropertyName("assignment_id")] public long AssignmentId { get; set; }
    [JsonPropertyName("assignment_title")] public string? AssignmentTitle { get; set; }
    [JsonPropertyName("section")] public string? Section { get; set; }
    [JsonPropertyName("evaluation_id")] public long? EvaluationId { get; set; }
    [JsonPropertyName("repo_name")] public string? RepoName { get; set; }
    [JsonPropertyName("repo_url")] public string? RepoUrl { get; set; }
    [JsonPropertyName("accepted_at")] public string? AcceptedAt { get; set; }
}

// Registro de entrega formal de un repo por parte de un alumno.
// Se persiste via RPC record_submission y se lee de assignment_submissions.
// Aceptar una tarea (Acceptance) != entregarla (Submission).
public class Submission
{
    [JsonPropertyName("assignment_id")] public long AssignmentId { get; set; }
    [JsonPropertyName("github_username")] public string GithubUsername { get; set; } = "";
    [JsonPropertyName("repo_url")] public string RepoUrl { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "submitted";
    [JsonPropertyName("submitted_at")] public string? SubmittedAt { get; set; }
}

// Estado calculado de una tarea para el alumno actual (cruce repo + acceptances).
// No mapea a ninguna tabla; es un view-model para la lista de tareas.
public class AssignmentStatus
{
    public Assignment Assignment { get; set; } = new();
    public bool Accepted { get; set; }
    public string? RepoName { get; set; }
    public string? RepoUrl { get; set; }

    // Entrega formal (assignment_submissions). Aceptar != Entregar.
    public bool Submitted { get; set; }
    public string? SubmittedRepoUrl { get; set; }
    public string? SubmittedAt { get; set; }

    // Bindings para la UI (DataTemplate de AssignmentsWindow).
    public string Title => Assignment.Title;
    public string? ClassroomUrl => Assignment.ClassroomUrl;
    public string StatusLabel => Submitted ? "Entregada ✓" : Accepted ? "Aceptada ✓" : "Pendiente";
    public bool IsPending => !Accepted && !string.IsNullOrEmpty(Assignment.ClassroomUrl);
    public bool HasRepoLink => Accepted && !string.IsNullOrEmpty(RepoUrl);
    public bool CanSubmit => Accepted || Assignment.AllowsManualSubmission;

    // Binding para XAML: mostrar boton "Entregar" solo cuando se puede y no se entrego.
    [JsonIgnore]
    public bool CanSubmitAndNotSubmitted => CanSubmit && !Submitted;

    // Naranja para pendiente, verde para aceptada (tema Consola Ops).
    [JsonIgnore]
    public System.Windows.Media.Brush BadgeBrush => Accepted
        ? new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#16A34A"))
        : new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D97706"));
}

public class TargetedLockdown
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("pc_name")] public string PcName { get; set; } = "";
    [JsonPropertyName("github_username")] public string GithubUsername { get; set; } = "";
    [JsonPropertyName("active")] public bool Active { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

// Contexto que el navegador embebido necesita para registrar la navegacion
// (tracking a browser_history) y para disparar la trampa con datos del alumno.
public sealed class BrowseContext
{
    public string GithubUsername { get; set; } = "";
    public string PcName { get; set; } = "";
    public string? Section { get; set; }
    public long? SectionId { get; set; }
}

public class ProcessInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("pid")] public int Pid { get; set; }
}

// Fila de la tabla `suspicious_processes`. section=NULL => regla global;
// section=X => regla extra de la seccion X. El cliente solo lee process_name
// para armar el HashSet del blocklist (ver SupabaseClient.GetBlocklistAsync).
public class SuspiciousProcess
{
    [JsonPropertyName("process_name")] public string ProcessName { get; set; } = "";
    [JsonPropertyName("section")] public string? Section { get; set; }
    [JsonPropertyName("section_id")] public long? SectionId { get; set; }
}

// DTOs para repos de GitHub (deserializar /user/repos y /repos/{owner}/{repo})
public class GitHubRepo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
    [JsonPropertyName("private")] public bool Private { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("owner")] public GitHubOwner? Owner { get; set; }
}

public class GitHubOwner
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
}

public class GitHubUser
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("id")] public long Id { get; set; }
}

public class RepoInvitation
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("repository")] public GitHubRepo? Repository { get; set; }
    [JsonPropertyName("inviter")] public GitHubOwner? Inviter { get; set; }
}

// Respuestas del device flow OAuth
public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
    [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
}

public class AccessTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}
