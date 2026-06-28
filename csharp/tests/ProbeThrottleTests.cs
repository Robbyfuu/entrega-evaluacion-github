using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests de las decisiones PURAS de la sonda de red, extraidas
/// de MainWindow.CheckNetworkProbeAsync hacia <see cref="ProbeThrottle"/>.
///
/// Congelan dos reglas con reloj inyectado (deterministas):
///   - throttle de 30s entre corridas de la sonda (ShouldProbe).
///   - dedup de 5 min por (host+source) antes de re-reportar (ShouldReportHit).
///
/// Los limites se preservan EXACTO del original: el codigo saltaba con
/// <c>&lt; 30s</c> / <c>&lt; 5min</c>, asi que el instante EXACTO de la ventana
/// SI procede (probe) / SI reporta.
/// </summary>
public class ProbeThrottleTests
{
    private static readonly DateTime Now = new(2026, 6, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HitWindow = TimeSpan.FromMinutes(5);

    // ===== ShouldProbe: throttle de 30s =====

    [Theory]
    // segundos desde la ultima corrida -> procede?
    [InlineData(0, false)]      // recien corrio
    [InlineData(15, false)]     // dentro de la ventana
    [InlineData(29.999, false)] // justo antes del limite
    [InlineData(30, true)]      // limite EXACTO: el original saltaba con < 30 => 30 procede
    [InlineData(30.001, true)]  // pasada la ventana
    [InlineData(120, true)]     // bien pasada
    public void ShouldProbe_AppliesThirtySecondWindow(double secondsSinceLast, bool expected)
    {
        var last = Now - TimeSpan.FromSeconds(secondsSinceLast);
        Assert.Equal(expected, ProbeThrottle.ShouldProbe(last, Now, ProbeWindow));
    }

    [Fact]
    public void ShouldProbe_FirstProbe_FromMinValue_IsAllowed()
    {
        // Arranque: _lastNetProbeUtc = DateTime.MinValue => la primera sonda corre.
        Assert.True(ProbeThrottle.ShouldProbe(DateTime.MinValue, Now, ProbeWindow));
    }

    // ===== ShouldReportHit: dedup de 5 min por (host+source) =====

    [Fact]
    public void ShouldReportHit_KeyAbsent_Reports()
    {
        var map = new Dictionary<string, DateTime>();
        Assert.True(ProbeThrottle.ShouldReportHit(map, "host|dns", Now, HitWindow));
    }

    [Theory]
    // minutos desde el ultimo reporte de esa clave -> re-reporta?
    [InlineData(0, false)]      // recien reportado
    [InlineData(2, false)]      // dentro de la ventana
    [InlineData(4.999, false)]  // justo antes del limite
    [InlineData(5, true)]       // limite EXACTO: el original saltaba con < 5 => 5 reporta
    [InlineData(5.001, true)]   // pasada la ventana
    [InlineData(30, true)]      // bien pasada
    public void ShouldReportHit_AppliesFiveMinuteWindowPerKey(double minutesSinceLast, bool expected)
    {
        var key = "githubcopilot.com|dns";
        var map = new Dictionary<string, DateTime>
        {
            [key] = Now - TimeSpan.FromMinutes(minutesSinceLast),
        };
        Assert.Equal(expected, ProbeThrottle.ShouldReportHit(map, key, Now, HitWindow));
    }

    [Fact]
    public void ShouldReportHit_OtherKeyRecent_DoesNotSuppressThisKey()
    {
        // La dedup es POR clave: un hit reciente de otra clave no calla a esta.
        var map = new Dictionary<string, DateTime>
        {
            ["otra.com|tcp"] = Now - TimeSpan.FromSeconds(1),
        };
        Assert.True(ProbeThrottle.ShouldReportHit(map, "githubcopilot.com|dns", Now, HitWindow));
    }
}
