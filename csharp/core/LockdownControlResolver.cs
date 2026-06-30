namespace EntregaEvaluacion.Core;

/// <summary>
/// Accion de transicion para un bloqueo con edge detection: aplicar, soltar o
/// no tocar. Equivale a las dos ramas <c>if (desired &amp;&amp; !current)</c> /
/// <c>else if (!desired &amp;&amp; current)</c> del original (la tercera "no hacer
/// nada" es <see cref="None"/>).
/// </summary>
public enum BlockAction
{
    None,
    Block,
    Unblock
}

/// <summary>
/// Snapshot PURO del control EFECTIVO de la evaluacion (override por evaluacion
/// ?? global), ya resuelto por el cliente. Se pasa como <c>null</c> al resolver
/// cuando el control no se pudo resolver (red/null) para expresar degrade-closed.
/// No incluye el mensaje del profesor: ese es un concern aparte que vive en la
/// vista (dedup + MessageBox).
/// </summary>
public readonly record struct LockdownControlInputs(bool InternetBlock, bool ForceLockdown);

/// <summary>
/// Decision resuelta: la transicion de internet, la de copilot (mismo toggle que
/// internet) y si corresponde mostrar la pantalla roja remota.
/// </summary>
public readonly record struct LockdownControlDecision(
    BlockAction Internet,
    BlockAction Copilot,
    bool ShouldShowRemoteRedScreen);

/// <summary>
/// Algebra PURA de resolucion del control de lockdown, extraida de
/// MainWindow.CheckAdminConfigAsync. Sin WPF, sin I/O, sin reloj: el caller
/// (LockdownCoordinator) hace los fetches, conserva los flags de estado y ejecuta
/// los efectos (Block/Unblock, suscripcion del watcher de Copilot, modal de
/// pantalla roja, heartbeat).
///
/// Preserva EXACTO el branching del original:
/// <list type="bullet">
///   <item>degrade-closed: <c>control == null</c> => todas las acciones en None
///   (no se suelta un bloqueo activo).</item>
///   <item><c>effInternet = InternetBlock &amp;&amp; !(unblockInternet ?? false)</c>;
///   internet y copilot comparten ese toggle.</item>
///   <item>edge detection por flag: Block si deseado y no aplicado; Unblock si no
///   deseado y aplicado; None si ya coinciden.</item>
///   <item>pantalla roja remota: <c>ForceLockdown &amp;&amp; inExam &amp;&amp;
///   !(unblockScreen ?? false) &amp;&amp; !remoteLockdownActive</c>.</item>
/// </list>
/// Fail-safe: <paramref name="unblockInternet"/>/<paramref name="unblockScreen"/>
/// null (override por PC no resuelto) se tratan como <c>false</c> via <c>?? false</c>,
/// es decir NO desbloquean nada.
/// </summary>
public static class LockdownControlResolver
{
    public static LockdownControlDecision Resolve(
        LockdownControlInputs? control,
        bool? unblockInternet,
        bool? unblockScreen,
        bool inExam,
        bool internetBlocked,
        bool copilotBlocked,
        bool remoteLockdownActive)
    {
        // Degrade-closed: sin control efectivo no se toca el estado actual (no se
        // suelta un bloqueo activo). Espeja el `if (cfg == null) return;` previo.
        if (control is not { } cfg)
            return new LockdownControlDecision(BlockAction.None, BlockAction.None, false);

        // Override por PC: unblock_internet anula el bloqueo de ESTE equipo.
        // null (fetch fallido) => ?? false => no anula nada (fail-safe).
        bool effInternet = cfg.InternetBlock && !(unblockInternet ?? false);
        bool screenUnblocked = unblockScreen ?? false;

        var internet = Edge(desired: effInternet, current: internetBlocked);
        // Copilot amarrado al MISMO toggle que internet (effInternet), igual que el
        // original; no hay un effCopilot separado.
        var copilot = Edge(desired: effInternet, current: copilotBlocked);

        bool showRed = cfg.ForceLockdown && inExam && !screenUnblocked && !remoteLockdownActive;

        return new LockdownControlDecision(internet, copilot, showRed);
    }

    /// <summary>
    /// Edge detection puro: Block cuando se desea y no esta aplicado; Unblock
    /// cuando no se desea y esta aplicado; None cuando ya coinciden.
    /// </summary>
    private static BlockAction Edge(bool desired, bool current)
        => desired && !current ? BlockAction.Block
            : !desired && current ? BlockAction.Unblock
            : BlockAction.None;
}
