using EntregaEvaluacion.Core;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Todo lo que <c>new CheatWindow(...)</c> necesita para abrir la pantalla roja,
/// proyectado a un POCO sin WPF para que el coordinador (Services) no dependa de
/// la ventana (Windows). El host (MainWindow) lo mapea 1:1 al constructor de
/// CheatWindow.
///
/// <see cref="SetOwner"/> preserva la diferencia EXACTA del original: la trampa
/// local fijaba <c>alert.Owner = this</c>, pero las pantallas remota y dirigida
/// NO lo fijaban. Mover esto al request mantiene ese comportamiento por-ruta.
/// </summary>
public sealed record RedScreenRequest(
    string RepoName,
    int FilesCount,
    string[] FilesNames,
    bool IsPersistent,
    bool RemoteSource,
    Func<bool>? CheckStillLocked,
    Action? OnHeartbeat,
    bool SetOwner);

/// <summary>
/// Seam de vista para la pantalla roja modal. El coordinador (Services) decide
/// CUANDO y CON QUE bloquear; el host (MainWindow) construye el CheatWindow en el
/// hilo de UI y lo muestra modal (ShowDialog), bloqueando hasta que se cierra.
/// El resultado del modal nunca se consume (el original tambien ignoraba el
/// retorno de ShowDialog); se devuelve solo para honrar el contrato del seam.
/// </summary>
public interface IRedScreenHost
{
    bool ShowBlocking(RedScreenRequest req);
}

/// <summary>
/// Resultado de aplicar el control efectivo. Distingue degrade-closed (control
/// no resoluble: la vista NO debe tocar el dedup del mensaje del profesor) de
/// control presente (con su mensaje, posiblemente null/vacio, para el dedup).
/// </summary>
public readonly struct AdminControlResult
{
    public bool ControlPresent { get; }
    public string? Message { get; }

    private AdminControlResult(bool present, string? message)
    {
        ControlPresent = present;
        Message = message;
    }

    // Degrade-closed: el control no se pudo resolver (red/null). La vista debe
    // hacer un return temprano sin tocar _lastAdminMessage.
    public static readonly AdminControlResult DegradeClosed = new(false, null);

    public static AdminControlResult Present(string? message) => new(true, message);
}

/// <summary>
/// Coordinador del LOCKDOWN / pantalla roja (ENT-8 slice K), extraido de
/// MainWindow. Es el UNICO duenio de los 4 flags de lockdown
/// (<c>_internetBlocked</c>, <c>_copilotBlocked</c>, <c>_remoteLockdownActive</c>,
/// <c>_targetedLockdownActive</c>) y de la orquestacion de la pantalla roja
/// (remota, dirigida y trampa local), el bloqueo de internet/Copilot y la
/// suscripcion del watcher de Copilot.
///
/// La DECISION del control es pura y vive en <see cref="LockdownControlResolver"/>
/// (testeada). Aqui se conservan los flags, se hacen los fetches (via
/// <see cref="ISupabaseClient"/>), se ejecutan los efectos (servicios estaticos
/// Internet/Copilot/Lockdown) y se abre la pantalla roja a traves del seam
/// <see cref="IRedScreenHost"/>. La secuencia
/// <c>flag=true -&gt; host.ShowBlocking (bloquea hasta cerrar) -&gt; flag=false</c>
/// queda INTERNA y atomica: como ShowBlocking corre un pump anidado y el AdminTick
/// re-entra, el flag ya esta en true cuando se re-evalua la decision, evitando una
/// segunda pantalla (paridad exacta con el original).
///
/// Concerns que NO se mueven: el dedup + MessageBox del mensaje del profesor y el
/// <c>_lastAdminMessage</c> quedan en la vista (ApplyControlAsync devuelve el
/// mensaje); gh.Logout / SetIdentityToken / ClearSelectors / UpdateSessionPanel
/// del cierre de evaluacion quedan en la vista (solo el slice internet/Copilot
/// pasa por <see cref="ReleaseForExamEnd"/>).
/// </summary>
public sealed class LockdownCoordinator
{
    private readonly ISupabaseClient _sb;
    private readonly ISelectionStore _selection;
    private readonly ILogSink _log;
    private readonly IUserNotifier _notifier;
    private readonly Func<Task> _sendHeartbeat;
    private readonly IRedScreenHost _redScreen;
    private readonly Func<GitHubUser?> _currentUser;

    // Estado de lockdown: este coordinador es el unico duenio (antes vivian en
    // MainWindow). La vista los lee solo via IsLockdownActive (para el heartbeat).
    private bool _internetBlocked;
    private bool _copilotBlocked;
    private bool _remoteLockdownActive;
    private bool _targetedLockdownActive;

    public LockdownCoordinator(
        ISupabaseClient sb,
        ISelectionStore selection,
        ILogSink log,
        IUserNotifier notifier,
        Func<Task> sendHeartbeat,
        IRedScreenHost redScreen,
        Func<GitHubUser?> currentUser)
    {
        _sb = sb;
        _selection = selection;
        _log = log;
        _notifier = notifier;
        _sendHeartbeat = sendHeartbeat;
        _redScreen = redScreen;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Estado de lockdown para el heartbeat: "active" si hay pantalla roja remota
    /// o dirigida/trampa activa. Espeja EXACTO
    /// <c>(_remoteLockdownActive || _targetedLockdownActive)</c> del original, que
    /// ahora vive aca.
    /// </summary>
    public bool IsLockdownActive => _remoteLockdownActive || _targetedLockdownActive;

    /// <summary>
    /// Aplica el control EFECTIVO (override por evaluacion ?? global) de la
    /// evaluacion actual: bloquea/desbloquea internet y Copilot y abre la pantalla
    /// roja remota si corresponde. Degrade-closed: si el control no se puede
    /// resolver (null) retorna SIN tocar nada (no se consulta el override por PC,
    /// paridad de I/O con el original) y sin tocar el dedup del mensaje (la vista
    /// hace return temprano). Devuelve el mensaje del profesor (cfg.Message) para
    /// que la vista haga el dedup + MessageBox.
    /// </summary>
    public async Task<AdminControlResult> ApplyControlAsync()
    {
        // Control EFECTIVO de la evaluacion actual. La APERTURA (aca) y la
        // LIBERACION (IsForceLockdownAsync en checkStillLocked) leen la MISMA
        // resolucion y comparten el cache _lastKnownLock: esta llamada ya SIEMBRA
        // el cache, asi el primer poll de liberacion -aunque su fetch falle-
        // retiene el lock recien aplicado en vez de soltarlo.
        var cfg = await _sb.GetEffectiveControlAsync(_selection.EvaluationId);
        if (cfg == null) return AdminControlResult.DegradeClosed;

        // Override por PC (desbloqueo por nombre de equipo). Fail-safe: si el
        // fetch falla, ovr es null -> no se libera nada (el resolver coalesce a false).
        var ovr = await _sb.GetPcOverrideAsync(Environment.MachineName);

        // La pantalla roja remota SOLO salta en modo evaluacion: el force_lockdown
        // global no debe bloquear a un alumno que no esta rindiendo.
        bool inExam = _selection.EvaluationId is { } examEvalId && examEvalId > 0;

        var decision = LockdownControlResolver.Resolve(
            new LockdownControlInputs(cfg.InternetBlock, cfg.ForceLockdown),
            ovr?.UnblockInternet, ovr?.UnblockScreen, inExam,
            _internetBlocked, _copilotBlocked, _remoteLockdownActive);

        // Internet.
        if (decision.Internet == BlockAction.Block)
        {
            _log.Log("[ADMIN] Bloqueo de internet activado.");
            InternetBlockService.Block();
            _internetBlocked = true;
        }
        else if (decision.Internet == BlockAction.Unblock)
        {
            _log.Log("[ADMIN] Bloqueo de internet desactivado.");
            InternetBlockService.Unblock();
            _internetBlocked = false;
        }

        // Copilot: amarrado al mismo toggle que internet. La suscripcion al watcher
        // (+=) va GATEADA por _copilotBlocked (== decision.Copilot Block) y la
        // baja (-=) por el mismo flag (== decision.Copilot Unblock), igual que el
        // original.
        if (decision.Copilot == BlockAction.Block)
        {
            CopilotBlockService.OnCheatDetected += OnCopilotCheatDetected;
            CopilotBlockService.Block();
            _copilotBlocked = true;
            _log.Log("[ADMIN] Bloqueo de Copilot activado.");
        }
        else if (decision.Copilot == BlockAction.Unblock)
        {
            CopilotBlockService.OnCheatDetected -= OnCopilotCheatDetected;
            CopilotBlockService.Unblock();
            _copilotBlocked = false;
            _log.Log("[ADMIN] Bloqueo de Copilot desactivado.");
        }

        // Pantalla roja remota (force-only). flag=true -> ShowBlocking -> flag=false,
        // atomico ante reentrancia del AdminTick (el modal corre un pump anidado).
        if (decision.ShouldShowRemoteRedScreen)
        {
            _remoteLockdownActive = true;
            _log.Log("[ADMIN] Lockdown remoto activado.");
            // Heartbeat inmediato (fire-and-forget) para que el panel vea a ESTE PC
            // bloqueado al instante; el AdminTick queda detenido tras ShowBlocking.
            _ = _sendHeartbeat();
            _redScreen.ShowBlocking(new RedScreenRequest(
                "(remoto)", 0, new[] { "Lockdown remoto del profesor" },
                IsPersistent: false, RemoteSource: true,
                CheckStillLocked: StillLockedByForce,
                OnHeartbeat: () => _ = _sendHeartbeat(),
                SetOwner: false));
            _remoteLockdownActive = false;
        }

        return AdminControlResult.Present(cfg.Message);
    }

    /// <summary>
    /// Pantalla roja por lockdown DIRIGIDO (pc+usuario). Espeja
    /// CheckTargetedLockdownAsync: gate por _targetedLockdownActive y sesion, lee
    /// el lock dirigido, abre la pantalla y la libera cuando el profe apaga el lock
    /// (o force). No fija Owner (paridad con el original).
    /// </summary>
    public async Task CheckTargetedAsync()
    {
        var user = _currentUser();
        if (_targetedLockdownActive || user == null) return;
        var locked = await _sb.IsTargetedLockedAsync(Environment.MachineName, user.Login);
        if (locked)
        {
            _targetedLockdownActive = true;
            var reason = await _sb.GetTargetedReasonAsync(Environment.MachineName, user.Login) ?? "El profesor te bloqueo";
            _log.Log("[ADMIN] Lockdown DIRIGIDO a tu PC.");
            var me = user.Login;
            _ = _sendHeartbeat();
            _redScreen.ShowBlocking(new RedScreenRequest(
                "(dirigido)", 0, new[] { reason },
                IsPersistent: false, RemoteSource: true,
                CheckStillLocked: () => StillLockedByTargetOrForce(Environment.MachineName, me),
                OnHeartbeat: () => _ = _sendHeartbeat(),
                SetOwner: false));
            _targetedLockdownActive = false;
        }
    }

    /// <summary>
    /// Pantalla roja por TRAMPA LOCAL (repo sucio, navegacion prohibida), VISIBLE
    /// en el panel y LIBERABLE remoto. Reusa _targetedLockdownActive (visible como
    /// "active" en el heartbeat y evita que CheckTargetedAsync abra una segunda
    /// pantalla). Si el reporte de auto-lock se confirma, la pantalla re-chequea y
    /// se libera remoto; si FALLA (offline), cae a password-only (fail-safe). Fija
    /// Owner=this en ambas ramas (paridad con el original).
    /// </summary>
    public async Task ShowLocalTrapAsync(string reasonOrRepo, int filesCount, string[] filesNames)
    {
        if (_targetedLockdownActive) return; // ya hay pantalla roja activa
        _targetedLockdownActive = true;

        LockdownService.Trigger(reasonOrRepo, filesCount, filesNames);

        var me = _currentUser()?.Login ?? "";
        var pc = Environment.MachineName;
        try
        {
            await _sb.ReportCheatEventAsync(
                me.Length > 0 ? me : "(sin sesion)", pc, reasonOrRepo, filesCount, filesNames);
        }
        catch { }

        bool reported = await _sb.ReportSelfLockAsync(
            pc, me, _selection.SectionText, filesNames.FirstOrDefault() ?? reasonOrRepo);

        _ = _sendHeartbeat();
        var req = reported
            ? new RedScreenRequest(
                reasonOrRepo, filesCount, filesNames,
                IsPersistent: false, RemoteSource: true,
                CheckStillLocked: () => StillLockedByTargetOrForce(pc, me),
                OnHeartbeat: () => _ = _sendHeartbeat(),
                SetOwner: true)
            : new RedScreenRequest(
                reasonOrRepo, filesCount, filesNames,
                IsPersistent: false, RemoteSource: false,
                CheckStillLocked: null, OnHeartbeat: null,
                SetOwner: true);
        _redScreen.ShowBlocking(req);

        _targetedLockdownActive = false;
    }

    /// <summary>
    /// Libera internet/Copilot de ESTE PC al cerrar la evaluacion. Internet se
    /// suelta INCONDICIONALMENTE (en try/catch) + _internetBlocked=false; Copilot
    /// solo si estaba bloqueado (baja del watcher gateada por _copilotBlocked).
    /// El resto del cierre (gh.Logout, SetIdentityToken, ClearSelectors,
    /// UpdateSessionPanel) queda en la vista.
    /// </summary>
    public void ReleaseForExamEnd()
    {
        try { InternetBlockService.Unblock(); _internetBlocked = false; } catch { }
        try
        {
            if (_copilotBlocked)
            {
                CopilotBlockService.OnCheatDetected -= OnCopilotCheatDetected;
                CopilotBlockService.Unblock();
                _copilotBlocked = false;
            }
        }
        catch { }
    }

    /// <summary>
    /// Handler del FileSystemWatcher de CopilotBlockService: el alumno edito el
    /// settings.json para reactivar Copilot. Reporta el cheat al panel y aplica
    /// lockdown inmediato. Se invoca desde un thread del watcher; Log/ShowToast ya
    /// dispatchean al UI thread internamente. async void: es un callback de evento
    /// (Action), igual que el original.
    /// </summary>
    private async void OnCopilotCheatDetected()
    {
        _log.Log("[CHEAT] Intento de reactivacion de Copilot detectado.");

        // Reportar al panel via el mismo canal de alertas de procesos sospechosos.
        try
        {
            var user = _currentUser()?.Login ?? "(unknown)";
            await _sb.ReportProcessAlertAsync(
                user,
                Environment.MachineName,
                _selection.SectionText,
                "copilot-reactivation",
                "Intento de reactivar Copilot editando settings.json");
        }
        catch (Exception ex) { _log.Log($"[CHEAT] Reporte de Copilot fallo: {ex.Message}"); }

        // Lockdown inmediato en la maquina del alumno (marker + auto-start + TaskMgr off).
        try
        {
            LockdownService.Trigger("(copilot)", 0, new[] { "Reactivacion de Copilot en settings.json" });
            _log.Log("[CHEAT] Lockdown aplicado por reactivacion de Copilot.");
        }
        catch (Exception ex) { _log.Log($"[CHEAT] Lockdown por Copilot fallo: {ex.Message}"); }

        // Avisar al alumno.
        _notifier.ShowToast("Se detecto intento de reactivar Copilot. Prueba bloqueada.", ToastKind.Error);
    }

    // Override de pantalla por PC, en SINCRONO (para los checkStillLocked de las
    // CheatWindow: su timer de UI necesita un bool sin deadlockear el pump del
    // modal). true => el profe desbloqueo la pantalla de ESTE PC -> liberar.
    private bool ScreenUnblockedSync()
        => Task.Run(() => _sb.IsPcScreenUnblockedAsync(Environment.MachineName).GetAwaiter().GetResult())
            .GetAwaiter().GetResult();

    // Predicados de "sigue bloqueada la pantalla" para los checkStillLocked. Fail-
    // safe: solo libera si el profe desbloqueo ESTE PC por nombre
    // (ScreenUnblockedSync) Y el backend ya no reporta el lock.

    // Lockdown REMOTO (force-only).
    private bool StillLockedByForce()
        => !ScreenUnblockedSync()
            && Task.Run(() => _sb.IsForceLockdownAsync(_selection.EvaluationId)).GetAwaiter().GetResult();

    // Lockdown DIRIGIDO (pc+usuario) o force. pc varia: Environment.MachineName en
    // el dirigido remoto, el pc real en la trampa local.
    private bool StillLockedByTargetOrForce(string pc, string me)
        => !ScreenUnblockedSync()
            && Task.Run(() =>
                _sb.IsTargetedLockedAsync(pc, me).GetAwaiter().GetResult()
                || _sb.IsForceLockdownAsync(_selection.EvaluationId).GetAwaiter().GetResult()).GetAwaiter().GetResult();
}
