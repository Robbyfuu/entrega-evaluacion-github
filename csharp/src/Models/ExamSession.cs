using System.Text.Json.Serialization;

namespace EntregaEvaluacion.Models;

// ===== Maquina de estados de evaluacion (fundacion aislada) =====
// Modela el ciclo de vida de una sesion de examen del lado del cliente.
// Esta capa es pura: NO toca DB ni red. La persistencia local y la
// validacion de transiciones viven en Services/ExamSessionService.cs.
// El reloj de servidor (StartedAtServerUtc) manda; nunca se usa el reloj
// local para validar el inicio del examen.

// Estados del ciclo de vida de una sesion de examen.
// Flujo feliz: Idle -> Preflight -> Ready -> ExamActive ->
//              SubmissionOpening -> Submitting -> Completed.
// Estados excepcionales:
//   RecoveryRequired  => se detecto una sesion previa que requiere recuperacion.
//   AbortedByTeacher  => el profe corto la evaluacion (control remoto).
public enum ExamState
{
    Idle,
    Preflight,
    Ready,
    ExamActive,
    SubmissionOpening,
    Submitting,
    Completed,
    RecoveryRequired,
    AbortedByTeacher
}

// Estado de aplicacion de un control de seguridad (bloqueo de internet,
// lockdown, etc.). Applied = el control quedo activo; Failed = no se pudo
// aplicar; Unknown = no se pudo determinar (p. ej. verificacion incompleta).
public enum ControlResult
{
    Unknown,
    Applied,
    Failed
}

// Resultado de verificar un control puntual: el enum de estado mas un
// mensaje legible para diagnostico. Inmutable; se construye por control.
public sealed class ControlStatus
{
    [JsonPropertyName("control_name")] public string ControlName { get; init; } = "";
    [JsonPropertyName("result")] public ControlResult Result { get; init; } = ControlResult.Unknown;
    [JsonPropertyName("message")] public string? Message { get; init; }

    public static ControlStatus Applied(string controlName, string? message = null) =>
        new() { ControlName = controlName, Result = ControlResult.Applied, Message = message };

    public static ControlStatus Failed(string controlName, string? message = null) =>
        new() { ControlName = controlName, Result = ControlResult.Failed, Message = message };

    public static ControlStatus Unknown(string controlName, string? message = null) =>
        new() { ControlName = controlName, Result = ControlResult.Unknown, Message = message };
}

// Autorizacion del profe para una accion sensible (iniciar/abortar examen,
// abrir la ventana de entrega, etc.). Token + identidad del profe + momento
// de emision en UTC de servidor. Esta capa solo transporta el dato; la
// validacion del token contra el backend la hara una capa superior.
public sealed class TeacherAuthorization
{
    [JsonPropertyName("teacher_id")] public string TeacherId { get; init; } = "";
    [JsonPropertyName("token")] public string Token { get; init; } = "";
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("issued_at_server_utc")] public DateTime IssuedAtServerUtc { get; init; }
}

// Datos de inicio de una sesion de examen. serverUtc es la hora de servidor
// (NO el reloj local) usada para fijar StartedAtServerUtc. El resto son los
// identificadores del contexto curso > seccion > evaluacion mas la identidad
// del alumno y del repo base (baseline).
public sealed class StartExamRequest
{
    [JsonPropertyName("exam_session_id")] public string ExamSessionId { get; init; } = "";
    [JsonPropertyName("server_utc")] public DateTime ServerUtc { get; init; }
    [JsonPropertyName("course_id")] public long? CourseId { get; init; }
    [JsonPropertyName("section_id")] public long? SectionId { get; init; }
    [JsonPropertyName("evaluation_id")] public long? EvaluationId { get; init; }
    [JsonPropertyName("github_username")] public string? GithubUsername { get; init; }
    [JsonPropertyName("pc_name")] public string? PcName { get; init; }
    [JsonPropertyName("repo_url")] public string? RepoUrl { get; init; }
    [JsonPropertyName("baseline_sha")] public string? BaselineSha { get; init; }
}

// Estado serializable de una sesion de examen. Es el documento que se
// persiste en exam-session.json para sobrevivir a reinicios del cliente.
// Mutable a proposito: el service lo evoluciona campo a campo. Los nombres
// JSON siguen el estilo snake_case del resto de Dtos.cs.
public class ExamSession
{
    [JsonPropertyName("exam_session_id")] public string ExamSessionId { get; set; } = "";
    [JsonPropertyName("state")] public ExamState State { get; set; } = ExamState.Idle;

    // Hora de inicio en UTC de servidor (no reloj local). default(DateTime)
    // mientras la sesion no haya arrancado.
    [JsonPropertyName("started_at_server_utc")] public DateTime StartedAtServerUtc { get; set; }

    // Contexto curso > seccion > evaluacion (NULL hasta que se conozca).
    [JsonPropertyName("course_id")] public long? CourseId { get; set; }
    [JsonPropertyName("section_id")] public long? SectionId { get; set; }
    [JsonPropertyName("evaluation_id")] public long? EvaluationId { get; set; }

    // Identidad del alumno y de la maquina.
    [JsonPropertyName("github_username")] public string? GithubUsername { get; set; }
    [JsonPropertyName("pc_name")] public string? PcName { get; set; }

    // Repo de la entrega y commit base (baseline) capturado al iniciar.
    [JsonPropertyName("repo_url")] public string? RepoUrl { get; set; }
    [JsonPropertyName("baseline_sha")] public string? BaselineSha { get; set; }

    // Marca de la ultima actualizacion del documento (UTC). Util para
    // diagnostico de recuperacion; no participa de la validacion de inicio.
    [JsonPropertyName("updated_at_utc")] public DateTime? UpdatedAtUtc { get; set; }
}

// Contrato de un control de seguridad aplicable durante el examen
// (bloqueo de internet, lockdown de procesos, etc.). Aislado de la
// implementacion concreta para mantener la fundacion testeable.
public interface IExamControl
{
    // Aplica el control al iniciar/activar el examen.
    Task ApplyAsync(ExamSession s, CancellationToken ct);

    // Verifica si el control sigue activo y devuelve su estado.
    Task<ControlStatus> VerifyAsync(CancellationToken ct);

    // Revierte el control al terminar el examen (flujo normal).
    Task RestoreAsync(CancellationToken ct);

    // Restablece el control tras una recuperacion (sesion previa colgada).
    Task RecoverAsync(CancellationToken ct);
}
