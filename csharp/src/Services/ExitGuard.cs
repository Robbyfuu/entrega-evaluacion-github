using EntregaEvaluacion.Core;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Colaborador extraido de MainWindow.OnClosing + ReportCloseAttemptAsync
/// (ENT-8 slice O). El programa NO se cierra durante la evaluacion: el alumno no
/// puede salir del control (y el daemon lo relanzaria igual). Solo se cierra con
/// la clave del profesor; <c>_allowExit</c> pasa a true cuando la clave es
/// correcta. Es duenio del estado mutable que antes vivia en MainWindow:
/// <c>_allowExit</c> (latch de cierre autorizado) y <c>_lastCloseReport</c>
/// (throttle del reporte de intento de cierre).
///
/// La DECISION del gate es pura y vive en <see cref="ExitDecision"/>; el throttle
/// de 30s reusa <see cref="ProbeThrottle.ShouldProbe"/> (ambos testeados). Aqui se
/// conserva el estado, se ejecutan los efectos (toast, reporte best-effort, prompt
/// de clave) y se delegan los efectos especificos de la vista via callbacks:
/// <c>isUpdating</c> (UpdateService.IsApplying), <c>passwordPrompt</c> (modal de
/// clave del profesor), <c>onAuthorizedExit</c> (Unregister + DeleteAllDownloaded +
/// Shutdown) y <c>currentUser</c> (identidad del alumno). La frontera de
/// verificacion de la clave permanece en PasswordPromptWindow (intacta).
/// </summary>
public sealed class ExitGuard
{
    // Throttle del reporte de intento de cierre. Mismo valor que el original
    // (30s) para no spamear si el alumno aprieta la X varias veces.
    private static readonly TimeSpan ReportThrottle = TimeSpan.FromSeconds(30);

    private readonly ISupabaseClient _sb;
    private readonly ISelectionStore _selection;
    private readonly IUserNotifier _notifier;
    private readonly Func<bool> _isUpdating;
    private readonly Func<bool> _passwordPrompt;
    private readonly Action _onAuthorizedExit;
    private readonly Func<GitHubUser?> _currentUser;

    // El programa NO se cierra durante la evaluacion. _allowExit pasa a true
    // cuando la clave del profesor es correcta.
    private bool _allowExit;

    // Ultimo reporte de intento de cierre (UTC). MinValue => nunca se reporto:
    // el primer intento pasa el throttle.
    private DateTime _lastCloseReport = DateTime.MinValue;

    public ExitGuard(
        ISupabaseClient sb,
        ISelectionStore selection,
        IUserNotifier notifier,
        Func<bool> isUpdating,
        Func<bool> passwordPrompt,
        Action onAuthorizedExit,
        Func<GitHubUser?> currentUser)
    {
        _sb = sb;
        _selection = selection;
        _notifier = notifier;
        _isUpdating = isUpdating;
        _passwordPrompt = passwordPrompt;
        _onAuthorizedExit = onAuthorizedExit;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Maneja el intento de cierre de la ventana. Devuelve true si el caller
    /// (MainWindow.OnClosing) debe fijar <c>e.Cancel = true</c> para BLOQUEAR el
    /// cierre; false si el cierre debe permitirse.
    ///
    /// Preserva EXACTO el flujo del original: si el gate permite cerrar
    /// (ya autorizado, ya cancelado, o update aplicandose) no bloquea. Si no,
    /// avisa con un toast, dispara fire-and-forget el reporte del intento (con
    /// throttle), abre el prompt modal de clave y, si la clave es correcta,
    /// latchea <c>_allowExit</c> y ejecuta el cierre autorizado.
    /// </summary>
    public bool HandleClosing(bool alreadyCancelled)
    {
        if (ShouldAllowClose(alreadyCancelled))
            return false;

        // Bloquear el cierre + avisar + registrar el intento.
        _notifier.ShowToast("No puedes cerrar el programa durante la evaluacion. Intento registrado.", ToastKind.Error);
        _ = ReportCloseAttemptAsync();

        // Escape del profesor: clave correcta => cerrar de verdad (y sacar el
        // daemon para que no lo relance).
        if (_passwordPrompt())
        {
            _allowExit = true;
            _onAuthorizedExit();
        }
        return true;
    }

    /// <summary>
    /// Decision PURA (sin efectos) de si corresponde PERMITIR el cierre real: ya
    /// se autorizo con clave (<c>_allowExit</c>), otro handler ya cancelo
    /// (<paramref name="alreadyCancelled"/>) o hay un update aplicandose. Reusa
    /// <see cref="ExitDecision.ShouldAllowClose"/> (la MISMA logica que evalua
    /// <see cref="HandleClosing"/>). La consume MainWindow.OnClosing FUERA de
    /// evaluacion para dejar pasar el cierre autorizado (bandeja "Salir" ->
    /// HandleClosing -> Shutdown) y, si no, ocultar la ventana a la bandeja sin
    /// disparar el toast ni el reporte del intento.
    /// </summary>
    public bool ShouldAllowClose(bool alreadyCancelled)
        => ExitDecision.ShouldAllowClose(_allowExit, alreadyCancelled, _isUpdating());

    /// <summary>
    /// Reporta el intento de cierre al panel (queda en Actividad). Throttle de
    /// 30s para no spamear si el alumno aprieta la X varias veces. Best-effort:
    /// no reporta sin identidad y nunca lanza (try/catch).
    /// </summary>
    private async Task ReportCloseAttemptAsync()
    {
        var user = _currentUser();
        if (user == null) return;
        var now = DateTime.UtcNow;
        if (!ProbeThrottle.ShouldProbe(_lastCloseReport, now, ReportThrottle)) return;
        _lastCloseReport = now;
        try
        {
            await _sb.ReportStudentActivityAsync(
                "close_attempt", user.Login, user.Email, Environment.MachineName,
                _selection.SectionText, "", null, _selection.SectionId);
        }
        catch { }
    }
}
