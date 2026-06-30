using System.Text.Json.Serialization;

namespace EntregaEvaluacion.Models;

// Override por PC (tabla pc_overrides). Permite desbloquear internet/pantalla de
// UN equipo por nombre, sin depender del usuario (en el lab los PC rotan).
public class PcOverride
{
    [JsonPropertyName("pc_name")] public string PcName { get; set; } = "";
    [JsonPropertyName("unblock_internet")] public bool UnblockInternet { get; set; }
    [JsonPropertyName("unblock_screen")] public bool UnblockScreen { get; set; }
}

public class ControlState
{
    [JsonPropertyName("internet_block")] public bool InternetBlock { get; set; }
    [JsonPropertyName("force_lockdown")] public bool ForceLockdown { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("updated_by")] public string? UpdatedBy { get; set; }
    // El profe setea esto (= NOW()) desde el panel para pedir a los clientes
    // que actualicen. El cliente solo dispara el update si este timestamp es
    // POSTERIOR a su arranque (ver MainWindow.CheckUpdateRequestAsync), asi un
    // request viejo no relanza updates en cada arranque.
    [JsonPropertyName("update_requested_at")] public string? UpdateRequestedAt { get; set; }
}

// Override de control por evaluacion (tabla evaluation_control).
// A diferencia de ControlState (control global id=1), aca los campos son
// NULLABLE: NULL significa "heredar el valor del control global". El control
// EFECTIVO de una evaluacion se resuelve campo a campo como
// (override.campo ?? global.campo). Ver SupabaseClient.GetEvaluationControlAsync.
public class EvaluationControl
{
    [JsonPropertyName("evaluation_id")] public long EvaluationId { get; set; }
    [JsonPropertyName("internet_block")] public bool? InternetBlock { get; set; }
    [JsonPropertyName("force_lockdown")] public bool? ForceLockdown { get; set; }
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
    // Path del PDF de enunciado en Storage (bucket privado 'exam-pdfs'). NULL =>
    // sin PDF. El cliente lo descarga, lo abre y lo borra al terminar.
    [JsonPropertyName("exam_pdf_path")] public string? ExamPdfPath { get; set; }
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

    // Invitacion de repo pendiente (repository_invitations) asociada a esta
    // tarea por prefijo de slug. InvitationId es el id de la invitacion en
    // GitHub (para aceptarla); InvitationPending indica que hay una invitacion
    // viva que el alumno aun no acepta. Estas dos senales alimentan el banner
    // (bucket pendienteAceptar) y son independientes de Accepted/Submitted.
    public long? InvitationId { get; set; }
    public bool InvitationPending { get; set; }

    // Bindings para la UI (DataTemplate de AssignmentsWindow).
    public string Title => Assignment.Title;
    public string? ClassroomUrl => Assignment.ClassroomUrl;
    public string StatusLabel => Submitted ? "Entregada ✓"
        : Accepted ? "Aceptada ✓"
        : InvitationPending ? "Invitacion pendiente"
        : "Pendiente";
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

// Confirmacion de matricula del alumno actual contra el roster (enrollments).
// Se obtiene SOLO via la RPC get_my_enrollment (SECURITY DEFINER, no-PII): el
// cliente anon NO puede leer enrollments directo (RLS authenticated-only).
// La RPC devuelve unicamente campos de confirmacion (section_id, status, found),
// nunca full_name/email/blackboard_student_id.
//
// Confirmed distingue los tres estados que el cliente necesita:
//   - Confirmed=true,  Found=true  => matricula confirmada (match en roster).
//   - Confirmed=true,  Found=false => la RPC respondio que NO hay matricula.
//   - Confirmed=false             => no se pudo confirmar (red/parseo fallo);
//                                     NO es lo mismo que "no matriculado".
// El cliente NUNCA debe tratar Confirmed=false como un "no matriculado"
// definitivo: en ese caso se cae al comportamiento por defecto (sin endurecer
// EXPECTED y sin suprimir entregas pendientes).
public sealed class MyEnrollment
{
    public bool Found { get; init; }
    public long? SectionId { get; init; }
    public string? Status { get; init; }

    // true solo cuando la RPC respondio (con o sin match). false = no se pudo
    // confirmar (fallo de red/parseo): centinela "could not confirm".
    public bool Confirmed { get; init; }

    // Centinela: la RPC no respondio (fallo de red/parseo). No es "no matriculado".
    public static MyEnrollment CouldNotConfirm => new() { Confirmed = false, Found = false };
}

// Fila cruda devuelta por la RPC get_my_enrollment (PostgREST la entrega como
// arreglo de filas con estas columnas). Se mapea a MyEnrollment en el cliente.
public sealed class MyEnrollmentRow
{
    [JsonPropertyName("section_id")] public long? SectionId { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("found")] public bool Found { get; set; }
}

// Fila devuelta por la RPC get_exam_time(p_evaluation_id): hora autoritativa del
// servidor (server_now) y fin del examen (ends_at), ambos timestamptz ISO8601.
// Se mantienen como string? (igual que el resto de timestamps de estos DTOs); el
// parseo a DateTimeOffset lo hace el caller. Alimenta el ancla anti-tamper de
// ExamTimerService: el cliente NUNCA usa su reloj de pared para el transcurrido.
public class ExamTime
{
    [JsonPropertyName("server_now")] public string? ServerNow { get; set; }
    [JsonPropertyName("ends_at")] public string? EndsAt { get; set; }
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

// Fila de la tabla `allowed_urls` (allowlist del navegador embebido).
// section=NULL => regla global; section=X => extra de la seccion X.
// kind='domain' => match por sufijo de host (IsDomainAllowed);
// kind='exact_url' => match por prefijo de scheme://host/path (IsUrlAllowed).
// El cliente lee pattern+kind para armar la allowlist dinamica
// (ver SupabaseClient.GetAllowlistAsync y Config.IsUrlAllowed).
public class AllowedUrl
{
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "domain";
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
