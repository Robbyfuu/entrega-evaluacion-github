using EntregaEvaluacion.Core;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Colaborador del heartbeat extraido de MainWindow.SendHeartbeatAsync. Envuelve
/// dos cosas: (1) la deteccion de procesos NUEVOS y sospechosos vs el tick previo
/// (set-diff puro en <see cref="NewSuspiciousProcesses"/>) con su alerta por
/// proceso, y (2) el envio del heartbeat. Es duenio del prior-set
/// (<c>_lastProcSet</c>), que antes vivia en MainWindow.
///
/// Las STRINGS internetState/lockdownState se reciben ya resueltas: MainWindow
/// las deriva de sus flags privados de lockdown (decision D1: esos flags NO se
/// mueven). HeartbeatReporter nunca lee esos flags. Tampoco fetchea procesos ni
/// blocklist: el caller arma la lista (ProcessMonitor) y pasa el blocklist
/// vigente; la decision de sospecha se delega a ProcessMonitor.IsSuspicious para
/// no divergir del comportamiento original.
/// </summary>
public sealed class HeartbeatReporter
{
    private readonly ISupabaseClient _sb;

    // Claves "{Name}:{Pid}" vistas en el tick previo (ordinal, case-sensitive,
    // igual que el HashSet&lt;string&gt; por defecto del MainWindow original).
    // Arranca vacio: el primer tick reporta todo sospechoso visible.
    private IReadOnlySet<string> _lastProcSet = new HashSet<string>();

    public HeartbeatReporter(ISupabaseClient sb, ILogSink log)
    {
        _sb = sb;
        // log: parte del contrato de construccion (paridad con los demas
        // servicios extraidos), reservado para diagnostico futuro. La ruta de
        // heartbeat NO loguea a proposito: el SendHeartbeatAsync original era
        // silencioso y agregar Log() cambiaria StatusText/toasts (comportamiento
        // observable). Por eso `log` no se almacena (evita CS0414 y preserva la
        // semantica exacta).
    }

    /// <summary>
    /// Reporta presencia: detecta procesos nuevos sospechosos vs el tick previo,
    /// alerta cada uno UNA vez, actualiza el prior-set y envia el heartbeat. El
    /// caller pasa pcName/username/email/section (identidad), la lista de procesos
    /// ya armada, el blocklist vigente (null => fallback de Config dentro de
    /// ProcessMonitor.IsSuspicious) y las strings de estado ya derivadas.
    /// </summary>
    public async Task SendAsync(
        string pcName, string username, string? email, string? section,
        List<ProcessInfo> processes, IReadOnlySet<string>? blocklist,
        string internetState, string lockdownState,
        long? evaluationId, string? appVersion)
    {
        // Proyeccion al record puro para el set-diff (sin LINQ: el SDK WPF no
        // incluye System.Linq por defecto).
        var observed = new List<ObservedProcess>(processes.Count);
        foreach (var p in processes)
            observed.Add(new ObservedProcess(p.Name, p.Pid, p.Title));

        var diff = NewSuspiciousProcesses.Diff(
            observed, _lastProcSet, name => ProcessMonitor.IsSuspicious(name, blocklist));

        // Alerta por cada nuevo sospechoso, en el orden original (procs).
        foreach (var p in diff.NewlySuspicious)
            await _sb.ReportProcessAlertAsync(username, pcName, section, p.Name, p.Title);

        // El prior-set pasa a ser el conjunto COMPLETO de claves de este tick.
        _lastProcSet = diff.SeenKeys;

        await _sb.SendHeartbeatAsync(
            pcName, username, email, section, processes,
            internetState, lockdownState, evaluationId, appVersion);
    }
}
