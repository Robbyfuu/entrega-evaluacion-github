using System.Diagnostics;
using System.Text.Json;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Maquina de estados de la sesion de examen (fundacion aislada).
/// No toca DB ni red: solo valida transiciones contra una tabla explicita y
/// persiste el estado en %APPDATA%/EntregaEvaluacion/exam-session.json para
/// sobrevivir a reinicios del cliente. El reloj de servidor manda: StartAsync
/// fija StartedAtServerUtc desde StartExamRequest.ServerUtc, nunca desde el
/// reloj local. RecoverAsync es idempotente.
/// </summary>
public interface IExamSessionService
{
    // Estado actual en memoria. Idle cuando no hay sesion cargada.
    ExamState CurrentState { get; }

    // Sesion actual en memoria (puede ser null antes de StartAsync/LoadAsync).
    ExamSession? Current { get; }

    // Inicia la sesion usando la hora de servidor del request (no reloj local).
    Task<ExamSession> StartAsync(StartExamRequest request, CancellationToken ct);

    // Abre la ventana de entrega (transicion a SubmissionOpening -> Submitting).
    Task BeginSubmissionAsync(CancellationToken ct);

    // Cierra la sesion como completada tras una entrega exitosa.
    Task CompleteAsync(CancellationToken ct);

    // Aborta la sesion por orden del profe (control remoto).
    Task AbortAsync(TeacherAuthorization authorization, CancellationToken ct);

    // Recupera una sesion previa colgada. Idempotente: llamarla repetidas
    // veces deja el mismo estado y no falla.
    Task RecoverAsync(CancellationToken ct);
}

public sealed class ExamSessionService : IExamSessionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Tabla de transiciones VALIDAS. Clave: estado origen. Valor: conjunto de
    // estados destino permitidos. Cualquier transicion fuera de esta tabla
    // lanza InvalidOperationException (ver Transition).
    private static readonly IReadOnlyDictionary<ExamState, IReadOnlySet<ExamState>> AllowedTransitions =
        new Dictionary<ExamState, IReadOnlySet<ExamState>>
        {
            [ExamState.Idle] = new HashSet<ExamState>
            {
                ExamState.Preflight, ExamState.RecoveryRequired
            },
            [ExamState.Preflight] = new HashSet<ExamState>
            {
                ExamState.Ready, ExamState.RecoveryRequired, ExamState.AbortedByTeacher
            },
            [ExamState.Ready] = new HashSet<ExamState>
            {
                ExamState.ExamActive, ExamState.RecoveryRequired, ExamState.AbortedByTeacher
            },
            [ExamState.ExamActive] = new HashSet<ExamState>
            {
                ExamState.SubmissionOpening, ExamState.RecoveryRequired, ExamState.AbortedByTeacher
            },
            [ExamState.SubmissionOpening] = new HashSet<ExamState>
            {
                ExamState.Submitting, ExamState.RecoveryRequired, ExamState.AbortedByTeacher
            },
            [ExamState.Submitting] = new HashSet<ExamState>
            {
                ExamState.Completed, ExamState.RecoveryRequired, ExamState.AbortedByTeacher
            },
            // Estados terminales: sin transiciones de salida.
            [ExamState.Completed] = new HashSet<ExamState>(),
            [ExamState.AbortedByTeacher] = new HashSet<ExamState>(),
            // Recuperacion: solo puede volver a Idle (reset idempotente).
            [ExamState.RecoveryRequired] = new HashSet<ExamState>
            {
                ExamState.Idle
            }
        };

    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ExamSession? _current;

    // Constructor por defecto: persiste en %APPDATA%/EntregaEvaluacion.
    public ExamSessionService()
        : this(DefaultStatePath())
    {
    }

    // Constructor con ruta inyectable (testeabilidad: la fundacion no asume
    // %APPDATA% en entornos de prueba).
    public ExamSessionService(string statePath)
    {
        _statePath = statePath;
    }

    public ExamState CurrentState => _current?.State ?? ExamState.Idle;

    public ExamSession? Current => _current;

    public async Task<ExamSession> StartAsync(StartExamRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Al reiniciar relee el estado: si ya hay una sesion viva, no se
            // arranca encima (idempotencia de arranque). Una sesion previa no
            // terminada exige recuperacion antes de iniciar de nuevo.
            await LoadLockedAsync(ct).ConfigureAwait(false);
            if (_current is { } existing && existing.State != ExamState.Idle
                && existing.State != ExamState.Completed
                && existing.State != ExamState.AbortedByTeacher)
            {
                throw new InvalidOperationException(
                    $"Ya existe una sesion de examen en curso (estado {existing.State}). " +
                    "Recupere o complete la sesion previa antes de iniciar una nueva.");
            }

            var session = new ExamSession
            {
                ExamSessionId = request.ExamSessionId,
                State = ExamState.Idle,
                // La hora de servidor manda; nunca el reloj local.
                StartedAtServerUtc = request.ServerUtc,
                CourseId = request.CourseId,
                SectionId = request.SectionId,
                EvaluationId = request.EvaluationId,
                GithubUsername = request.GithubUsername,
                PcName = request.PcName,
                RepoUrl = request.RepoUrl,
                BaselineSha = request.BaselineSha
            };

            _current = session;

            // Idle -> Preflight -> Ready: el arranque deja la sesion lista.
            ApplyTransition(session, ExamState.Preflight);
            ApplyTransition(session, ExamState.Ready);

            await SaveLockedAsync(ct).ConfigureAwait(false);
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BeginSubmissionAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = RequireSession();

            // El examen pasa a activo si aun no lo esta, luego abre la entrega.
            if (session.State == ExamState.Ready)
            {
                ApplyTransition(session, ExamState.ExamActive);
            }

            ApplyTransition(session, ExamState.SubmissionOpening);
            ApplyTransition(session, ExamState.Submitting);

            await SaveLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = RequireSession();
            ApplyTransition(session, ExamState.Completed);
            await SaveLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AbortAsync(TeacherAuthorization authorization, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = RequireSession();
            ApplyTransition(session, ExamState.AbortedByTeacher);
            await SaveLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadLockedAsync(ct).ConfigureAwait(false);

            // Idempotente: sin sesion o ya en Idle, no hay nada que recuperar.
            if (_current is null)
            {
                return;
            }

            var session = _current;

            // Estados terminales no requieren recuperacion: se dejan como estan.
            if (session.State is ExamState.Completed or ExamState.AbortedByTeacher
                or ExamState.Idle)
            {
                return;
            }

            // Ya marcada para recuperacion: cerrar el ciclo volviendo a Idle.
            if (session.State == ExamState.RecoveryRequired)
            {
                ApplyTransition(session, ExamState.Idle);
                await SaveLockedAsync(ct).ConfigureAwait(false);
                return;
            }

            // Sesion en curso colgada: marcar recuperacion y volver a Idle.
            ApplyTransition(session, ExamState.RecoveryRequired);
            ApplyTransition(session, ExamState.Idle);
            await SaveLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ===== Internos =====

    private ExamSession RequireSession()
    {
        if (_current is null)
        {
            throw new InvalidOperationException(
                "No hay una sesion de examen activa. Llame a StartAsync primero.");
        }

        return _current;
    }

    // Valida y aplica una transicion contra la tabla explicita. Lanza
    // InvalidOperationException si la transicion no esta permitida.
    private static void ApplyTransition(ExamSession session, ExamState target)
    {
        var from = session.State;
        if (from == target)
        {
            return;
        }

        if (!AllowedTransitions.TryGetValue(from, out var targets) || !targets.Contains(target))
        {
            throw new InvalidOperationException(
                $"Transicion invalida de {from} a {target}.");
        }

        session.State = target;
        session.UpdatedAtUtc = DateTime.UtcNow;
    }

    // Relee el estado persistido a memoria (idempotente). Si no hay archivo,
    // deja _current como esta. No lanza ante JSON corrupto: lo registra y lo
    // ignora para no bloquear el arranque del cliente.
    private async Task LoadLockedAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            await using var stream = new FileStream(
                _statePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var loaded = await JsonSerializer
                .DeserializeAsync<ExamSession>(stream, SerializerOptions, ct)
                .ConfigureAwait(false);

            if (loaded is not null)
            {
                _current = loaded;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExamSessionService] No se pudo leer {_statePath}: {ex.Message}");
        }
    }

    // Persiste el estado actual a disco (escritura atomica via archivo temporal).
    private async Task SaveLockedAsync(CancellationToken ct)
    {
        if (_current is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _statePath + ".tmp";
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer
                    .SerializeAsync(stream, _current, SerializerOptions, ct)
                    .ConfigureAwait(false);
            }

            // Reemplazo atomico: evita dejar un JSON a medio escribir.
            File.Move(tempPath, _statePath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExamSessionService] No se pudo persistir {_statePath}: {ex.Message}");
        }
    }

    private static string DefaultStatePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "EntregaEvaluacion", "exam-session.json");
    }
}
