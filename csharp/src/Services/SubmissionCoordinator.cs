using EntregaEvaluacion.Core;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Resultado del push de la entrega. Espeja GitService.PushResult pero sin atar
/// al caller a ese tipo: Ok + URL del repo (para portapapeles/MessageBox) +
/// Error (diagnostico). La UI decide que mostrar; el coordinador no toca XAML.
/// </summary>
public sealed class PushOutcome
{
    public bool Ok { get; init; }
    public string? RepoUrl { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Orquesta la RUTA DE ENTREGA de la evaluacion (ENT-8 slice B), extraida de
/// MainWindow.SubirArchivosAsync para sacar la logica de negocio del code-behind:
///
///  - PushAsync: arma el mensaje de commit, construye el GitService via la
///    factory inyectada y corre CommitAndPush en un hilo de fondo (Task.Run),
///    devolviendo un PushOutcome. NO muestra UI (ni MessageBox ni portapapeles);
///    solo registra diagnostico via ILogSink/IUserNotifier (abstracciones, no XAML).
///  - RecordSubmission/RecordAcceptanceIfClassroomRepoAsync: registro best-effort
///    de la entrega/aceptacion contra la tarea de Classroom que matchea el repo.
///    El matching (exacto por nombre esperado + fallback a tarea unica) vive en
///    EntregaEvaluacion.Core.ClassroomAssignmentMatcher (puro y testeado).
///  - RecordSubmissionAsync: passthrough del registro de entrega manual (lo usa
///    el callback SubmitRepo de la UI, que sigue en MainWindow).
///
/// El registro es best-effort y NO bloqueante: un fallo aqui jamas debe abortar
/// ni ocultar un push exitoso (el caller mantiene el orden push -> URL -> record
/// -> cleanup, y el matching se hace contra GetSectionAssignmentsAsync que lee
/// _selection + _sb).
/// </summary>
public sealed class SubmissionCoordinator
{
    private readonly IGitHubService _gh;
    private readonly ISupabaseClient _sb;
    private readonly ISelectionStore _selection;
    private readonly ILogSink _log;
    private readonly IUserNotifier _notifier;
    private readonly Func<string, string, string, GitService> _gitFactory;

    public SubmissionCoordinator(
        IGitHubService gh, ISupabaseClient sb, ISelectionStore selection,
        ILogSink log, IUserNotifier notifier,
        Func<string, string, string, GitService> gitFactory)
    {
        _gh = gh;
        _sb = sb;
        _selection = selection;
        _log = log;
        _notifier = notifier;
        _gitFactory = gitFactory;
    }

    /// <summary>
    /// Sube la carpeta al repo owner/name: arma el mensaje de commit (nombre +
    /// tipo, igual que antes), construye el GitService via la factory inyectada
    /// (token vigente de _gh) y corre CommitAndPush en un hilo de fondo. Reporta
    /// progreso/diagnostico via ILogSink/IUserNotifier (no XAML) y devuelve el
    /// PushOutcome. repoLabel es la etiqueta cruda del repo (puede ser "owner/name"
    /// o solo "name") tal como la ve el alumno, para el mensaje de estado.
    /// </summary>
    public async Task<PushOutcome> PushAsync(
        string folder, string repoOwner, string repoName, string repoLabel,
        string displayName, string email, string? tipo)
    {
        _notifier.Status($"Subiendo a {repoLabel}...");
        _log.Log($"-> Subiendo {folder} a {repoOwner}/{repoName}");

        var msg = string.IsNullOrEmpty(tipo)
            ? $"Entrega de evaluacion - {displayName}"
            : $"Entrega de evaluacion - {displayName} ({tipo})";

        var git = _gitFactory(_gh.Token!, displayName, email);
        var res = await Task.Run(() => git.CommitAndPush(folder, repoOwner, repoName, msg));

        if (!res.Ok)
        {
            _log.Log($"Fallo push: {res.Error}");
            _notifier.Status("Error en push.");
            return new PushOutcome { Ok = false, Error = res.Error };
        }

        _log.Log($"OK Subida completada: {res.Url}");
        return new PushOutcome { Ok = true, RepoUrl = res.Url };
    }

    /// <summary>
    /// Tras subir al repo, registra la ENTREGA (assignment_submissions) capturando
    /// el enlace, para que el profe la vea en el panel sin que el alumno tenga que
    /// apretar "Entregar repo" aparte. Mapea el repo a la tarea por nombre esperado;
    /// si no hay match exacto pero hay UNA sola tarea activa para la evaluacion, usa
    /// esa (los slugs de Classroom no siempre coinciden con Sanitize(titulo)). No
    /// bloquea la subida: cualquier fallo se ignora.
    /// </summary>
    public async Task RecordSubmissionIfClassroomRepoAsync(string username, string repoName, string repoUrl)
    {
        if (string.IsNullOrEmpty(username)) return;
        try
        {
            var asg = await GetSectionAssignmentsAsync();
            if (asg.Count == 0) return;
            var match = ClassroomAssignmentMatcher.MatchByExpectedRepo(
                asg, repoName, username, a => a.Title, singleActiveFallback: true);
            if (match != null)
                await _sb.RecordSubmissionAsync(match.Id, username, repoUrl);
        }
        catch { }
    }

    /// <summary>
    /// Si el repo clonado corresponde a una tarea activa de Classroom de la
    /// seccion del alumno ({slug}-{username}), registra la aceptacion en BD. Asi
    /// queda registro aunque el alumno clone directo sin pasar por el banner. Sin
    /// fallback a tarea unica (paridad exacta con el comportamiento previo).
    /// </summary>
    public async Task RecordAcceptanceIfClassroomRepoAsync(string username, string repoName, string repoUrl)
    {
        if (string.IsNullOrEmpty(username)) return;
        var asg = await GetSectionAssignmentsAsync();
        var match = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, repoName, username, a => a.Title, singleActiveFallback: false);
        if (match != null)
            await _sb.RecordAcceptanceAsync(username, match.Id, match.Title,
                _selection.SectionText, repoName, repoUrl, _selection.EvaluationId);
    }

    /// <summary>
    /// Passthrough del registro de entrega manual (callback SubmitRepo de la UI).
    /// La UI (dialogo, pre-fill, MessageBox) sigue en MainWindow; solo el registro
    /// pasa por aca para mantener un unico duenio del puerto de entregas.
    /// </summary>
    public Task RecordSubmissionAsync(long assignmentId, string username, string repoUrl)
        => _sb.RecordSubmissionAsync(assignmentId, username, repoUrl);

    private List<Assignment> FilterBySection(List<Assignment> all)
    {
        var sec = _selection.SectionText.Trim().ToUpperInvariant();
        return all.Where(a =>
        {
            var s = (a.Section ?? "").Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(s) || (!string.IsNullOrEmpty(sec) && s == sec);
        }).ToList();
    }

    /// <summary>
    /// Tareas activas que el alumno DEBE ver. ROBUSTO al evaluation_id: trae todas
    /// las activas, filtra por SECCION, y si la evaluacion seleccionada tiene tareas
    /// las prioriza; si NO (link huerfano porque se recreo la evaluacion), cae a las
    /// de la seccion. La evaluacion es una preferencia, no un gate.
    /// </summary>
    public async Task<List<Assignment>> GetSectionAssignmentsAsync()
    {
        var all = FilterBySection(await _sb.GetActiveAssignmentsAsync(null));
        var evalId = _selection.EvaluationId;
        if (evalId is { } id)
        {
            var matched = all.Where(a => a.EvaluationId == id).ToList();
            if (matched.Count > 0) return matched;
        }
        return all;
    }
}
