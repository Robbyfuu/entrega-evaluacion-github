using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests del algebra PURA de resolucion de control de lockdown,
/// extraida de MainWindow.CheckAdminConfigAsync hacia
/// <see cref="LockdownControlResolver"/>.
///
/// Congela EXACTO el branching del original:
///   var cfg = await GetEffectiveControlAsync(...);
///   if (cfg == null) return;                                  // degrade-closed
///   bool effInternet  = cfg.InternetBlock &amp;&amp; !(ovr?.UnblockInternet ?? false);
///   bool screenUnblocked = ovr?.UnblockScreen ?? false;
///   if (effInternet &amp;&amp; !_internetBlocked) Block; else if (!effInternet &amp;&amp; _internetBlocked) Unblock;
///   if (effInternet &amp;&amp; !_copilotBlocked) Block(copilot); else if (!effInternet &amp;&amp; _copilotBlocked) Unblock(copilot);
///   if (cfg.ForceLockdown &amp;&amp; inExam &amp;&amp; !screenUnblocked &amp;&amp; !_remoteLockdownActive) ShowRedScreen;
///
/// Copilot es ADITIVO: se bloquea cuando effInternet O copilot_block (el flag
/// standalone del panel, independiente de internet) estan activos. Los tests
/// legacy usan CopilotBlock=false (default) => copilot==internet, igual que antes.
/// La decision es pura: sin WPF, sin I/O, sin reloj.
/// </summary>
public class LockdownControlResolverTests
{
    private static LockdownControlDecision Resolve(
        LockdownControlInputs? control,
        bool? unblockInternet,
        bool? unblockScreen,
        bool inExam,
        bool internetBlocked,
        bool copilotBlocked,
        bool remoteLockdownActive)
        => LockdownControlResolver.Resolve(
            control, unblockInternet, unblockScreen, inExam,
            internetBlocked, copilotBlocked, remoteLockdownActive);

    // ===== Invariante 4: degrade-closed — control null => NO soltar nada =====
    [Fact]
    public void NullControl_WithActiveBlocks_DoesNotRelease()
    {
        // Aunque internet y copilot esten bloqueados, sin control efectivo no se
        // libera nada (degrade-closed): toda accion es None y no hay pantalla.
        var d = Resolve(null, unblockInternet: null, unblockScreen: null, inExam: true,
            internetBlocked: true, copilotBlocked: true, remoteLockdownActive: false);

        Assert.Equal(BlockAction.None, d.Internet);
        Assert.Equal(BlockAction.None, d.Copilot);
        Assert.False(d.ShouldShowRemoteRedScreen);
    }

    [Fact]
    public void NullControl_NoBlocks_DoesNothing()
    {
        var d = Resolve(null, null, null, inExam: true,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.Equal(BlockAction.None, d.Internet);
        Assert.Equal(BlockAction.None, d.Copilot);
        Assert.False(d.ShouldShowRemoteRedScreen);
    }

    // ===== Internet + Copilot: mismo toggle (effInternet), con edge detection =====
    [Fact]
    public void InternetBlockOn_NotYetBlocked_BlocksBoth()
    {
        var d = Resolve(new LockdownControlInputs(InternetBlock: true, ForceLockdown: false),
            unblockInternet: null, unblockScreen: null, inExam: false,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.Equal(BlockAction.Block, d.Internet);
        Assert.Equal(BlockAction.Block, d.Copilot);
    }

    [Fact]
    public void InternetBlockOff_CurrentlyBlocked_UnblocksBoth()
    {
        var d = Resolve(new LockdownControlInputs(InternetBlock: false, ForceLockdown: false),
            null, null, inExam: false,
            internetBlocked: true, copilotBlocked: true, remoteLockdownActive: false);

        Assert.Equal(BlockAction.Unblock, d.Internet);
        Assert.Equal(BlockAction.Unblock, d.Copilot);
    }

    [Fact]
    public void InternetBlockOn_AlreadyBlocked_NoOp()
    {
        var d = Resolve(new LockdownControlInputs(true, false),
            null, null, inExam: false,
            internetBlocked: true, copilotBlocked: true, remoteLockdownActive: false);

        Assert.Equal(BlockAction.None, d.Internet);
        Assert.Equal(BlockAction.None, d.Copilot);
    }

    [Fact]
    public void InternetBlockOff_AlreadyFree_NoOp()
    {
        var d = Resolve(new LockdownControlInputs(false, false),
            null, null, inExam: false,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.Equal(BlockAction.None, d.Internet);
        Assert.Equal(BlockAction.None, d.Copilot);
    }

    [Fact]
    public void Copilot_IndependentEdge_BlocksWhenInternetAlreadyBlockedButCopilotNot()
    {
        // effInternet=true; internet ya bloqueado (None) pero copilot aun no =>
        // solo copilot se bloquea. Refleja la edge detection por-flag del original.
        var d = Resolve(new LockdownControlInputs(true, false),
            null, null, inExam: false,
            internetBlocked: true, copilotBlocked: false, remoteLockdownActive: false);

        Assert.Equal(BlockAction.None, d.Internet);
        Assert.Equal(BlockAction.Block, d.Copilot);
    }

    // ===== copilot_block ADITIVO: independiente de internet_block =====
    [Fact]
    public void CopilotBlockOn_InternetOff_BlocksOnlyCopilot()
    {
        // copilot_block=true, internet_block=false => se bloquea Copilot, NO internet.
        // El switch standalone del panel.
        var d = Resolve(new LockdownControlInputs(InternetBlock: false, ForceLockdown: false, CopilotBlock: true),
            null, null, inExam: false,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.Equal(BlockAction.None, d.Internet);
        Assert.Equal(BlockAction.Block, d.Copilot);
    }

    [Fact]
    public void CopilotBlockOn_PlusInternetOn_BlocksBoth()
    {
        var d = Resolve(new LockdownControlInputs(InternetBlock: true, ForceLockdown: false, CopilotBlock: true),
            null, null, inExam: false,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.Equal(BlockAction.Block, d.Internet);
        Assert.Equal(BlockAction.Block, d.Copilot);
    }

    [Fact]
    public void CopilotBlockOff_InternetOff_NoCopilotBlock()
    {
        // Sin internet ni copilot_block => Copilot no se bloquea (preserva el viejo).
        var d = Resolve(new LockdownControlInputs(InternetBlock: false, ForceLockdown: false, CopilotBlock: false),
            null, null, inExam: false,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.Equal(BlockAction.None, d.Copilot);
    }

    [Fact]
    public void CopilotBlockOn_IndependentOfPcUnblockInternet()
    {
        // unblock_internet por PC suelta internet, pero copilot_block es
        // INDEPENDIENTE => Copilot sigue bloqueado (None: ya estaba bloqueado).
        var d = Resolve(new LockdownControlInputs(InternetBlock: true, ForceLockdown: false, CopilotBlock: true),
            unblockInternet: true, unblockScreen: null, inExam: false,
            internetBlocked: true, copilotBlocked: true, remoteLockdownActive: false);

        Assert.Equal(BlockAction.Unblock, d.Internet);   // internet liberado por el PC
        Assert.Equal(BlockAction.None, d.Copilot);       // copilot_block lo mantiene
    }

    [Fact]
    public void CopilotBlockOff_InternetOff_ReleasesCopilotIfBlocked()
    {
        // Ni internet ni copilot_block, copilot estaba bloqueado => Unblock.
        var d = Resolve(new LockdownControlInputs(InternetBlock: false, ForceLockdown: false, CopilotBlock: false),
            null, null, inExam: false,
            internetBlocked: false, copilotBlocked: true, remoteLockdownActive: false);

        Assert.Equal(BlockAction.Unblock, d.Copilot);
    }

    // ===== Invariante 7: PC-override fail-safe =====
    [Fact]
    public void PcOverrideUnblockInternet_True_MakesEffInternetFalse()
    {
        // unblock_internet=true anula el bloqueo: si estaba bloqueado => Unblock.
        var d = Resolve(new LockdownControlInputs(InternetBlock: true, ForceLockdown: false),
            unblockInternet: true, unblockScreen: null, inExam: false,
            internetBlocked: true, copilotBlocked: true, remoteLockdownActive: false);

        Assert.Equal(BlockAction.Unblock, d.Internet);
        Assert.Equal(BlockAction.Unblock, d.Copilot);
    }

    [Fact]
    public void PcOverrideNull_DoesNotUnblock_FailSafe()
    {
        // ovr null (fetch fallido) => unblockInternet=null => NO se anula el
        // bloqueo: con InternetBlock=true y sin estar bloqueado aun => Block.
        var d = Resolve(new LockdownControlInputs(true, true),
            unblockInternet: null, unblockScreen: null, inExam: true,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.Equal(BlockAction.Block, d.Internet);
        // Y la pantalla roja sigue mostrandose (screen no desbloqueada).
        Assert.True(d.ShouldShowRemoteRedScreen);
    }

    // ===== Invariante 6: inExam gate =====
    [Fact]
    public void ForceLockdown_NotInExam_NoRedScreen()
    {
        var d = Resolve(new LockdownControlInputs(InternetBlock: false, ForceLockdown: true),
            null, null, inExam: false,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.False(d.ShouldShowRemoteRedScreen);
    }

    [Fact]
    public void ForceLockdown_InExam_ScreenLocked_ShowsRedScreen()
    {
        var d = Resolve(new LockdownControlInputs(false, ForceLockdown: true),
            unblockInternet: null, unblockScreen: null, inExam: true,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.True(d.ShouldShowRemoteRedScreen);
    }

    [Fact]
    public void NoForceLockdown_InExam_NoRedScreen()
    {
        var d = Resolve(new LockdownControlInputs(false, ForceLockdown: false),
            null, null, inExam: true,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.False(d.ShouldShowRemoteRedScreen);
    }

    // ===== PC-override de pantalla =====
    [Fact]
    public void UnblockScreen_True_SuppressesRedScreen()
    {
        var d = Resolve(new LockdownControlInputs(false, ForceLockdown: true),
            unblockInternet: null, unblockScreen: true, inExam: true,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: false);

        Assert.False(d.ShouldShowRemoteRedScreen);
    }

    // ===== Invariante 5: una sola pantalla — guard de reentrancia =====
    [Fact]
    public void RemoteLockdownAlreadyActive_DoesNotShowSecondScreen()
    {
        var d = Resolve(new LockdownControlInputs(false, ForceLockdown: true),
            unblockInternet: null, unblockScreen: null, inExam: true,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: true);

        Assert.False(d.ShouldShowRemoteRedScreen);
    }

    // ===== Matriz de la pantalla roja remota: las 4 banderas que la gatean =====
    [Theory]
    // force, inExam, screenUnblocked, remoteActive -> show?
    [InlineData(true, true, false, false, true)]   // todas alineadas => muestra
    [InlineData(false, true, false, false, false)] // sin force
    [InlineData(true, false, false, false, false)] // fuera de examen
    [InlineData(true, true, true, false, false)]   // pantalla desbloqueada por PC
    [InlineData(true, true, false, true, false)]   // ya hay pantalla activa
    public void RedScreenMatrix(bool force, bool inExam, bool screenUnblocked, bool remoteActive, bool expected)
    {
        var d = Resolve(new LockdownControlInputs(InternetBlock: false, ForceLockdown: force),
            unblockInternet: null, unblockScreen: screenUnblocked, inExam: inExam,
            internetBlocked: false, copilotBlocked: false, remoteLockdownActive: remoteActive);

        Assert.Equal(expected, d.ShouldShowRemoteRedScreen);
    }
}
