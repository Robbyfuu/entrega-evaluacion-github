using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests de la decision PURA del guard de cierre, extraida de
/// MainWindow.OnClosing hacia <see cref="ExitDecision"/>.
///
/// Congela EXACTO el gate del original:
///   <c>if (_allowExit || e.Cancel || UpdateService.IsApplying) return;</c>
/// es decir: se PERMITE cerrar (return temprano, sin bloquear) si CUALQUIERA de
/// los tres es true. En cualquier otro caso (los tres false) se BLOQUEA el cierre.
///
/// La decision NO lee el reloj ni toca estado: es un OR puro de tres banderas.
/// </summary>
public class ExitDecisionTests
{
    [Theory]
    // allowExit, alreadyCancelled, isUpdating -> permitir cierre?
    [InlineData(false, false, false, false)] // nada autoriza => BLOQUEA
    [InlineData(true, false, false, true)]    // ya autorizado con clave => permite
    [InlineData(false, true, false, true)]    // otro handler ya cancelo => permite
    [InlineData(false, false, true, true)]    // update aplicandose (Velopack) => permite
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, true)]
    public void ShouldAllowClose_IsOrOfTheThreeFlags(
        bool allowExit, bool alreadyCancelled, bool isUpdating, bool expected)
    {
        Assert.Equal(expected, ExitDecision.ShouldAllowClose(allowExit, alreadyCancelled, isUpdating));
    }

    [Fact]
    public void AllFlagsFalse_BlocksClose()
    {
        // Caso central de integridad: sin clave, sin cancelacion previa y sin
        // update en curso, el cierre se BLOQUEA (no hay gate de "en evaluacion").
        Assert.False(ExitDecision.ShouldAllowClose(false, false, false));
    }

    [Fact]
    public void AllowExit_AllowsClose()
        => Assert.True(ExitDecision.ShouldAllowClose(allowExit: true, alreadyCancelled: false, isUpdating: false));

    [Fact]
    public void AlreadyCancelled_AllowsClose()
        => Assert.True(ExitDecision.ShouldAllowClose(allowExit: false, alreadyCancelled: true, isUpdating: false));

    [Fact]
    public void IsUpdating_AllowsClose()
        => Assert.True(ExitDecision.ShouldAllowClose(allowExit: false, alreadyCancelled: false, isUpdating: true));
}
