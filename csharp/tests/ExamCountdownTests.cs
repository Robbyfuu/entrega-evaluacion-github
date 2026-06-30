using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Tests del calculador PURO del countdown anti-tamper (<see cref="ExamCountdown"/>).
///
/// La regla es: remaining = (endsAt - serverNowAtSync) - elapsedSinceSync,
/// SIEMPRE clampeado a TimeSpan.Zero (nunca negativo). El elapsed lo provee el
/// caller desde un Stopwatch MONOTONICO (no el reloj de pared), de modo que un
/// cambio del reloj del sistema NO puede alargar el examen. Aca el elapsed se
/// inyecta como TimeSpan para que el calculo sea determinista y testeable sin
/// reloj ni I/O.
/// </summary>
public class ExamCountdownTests
{
    // Ancla de servidor fija; todos los casos parten de aca.
    private static readonly DateTimeOffset ServerNow =
        new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Remaining_FarFromEnd_ReturnsFullWindowWhenNoElapsed()
    {
        var endsAt = ServerNow.AddMinutes(60);
        var remaining = ExamCountdown.Remaining(ServerNow, endsAt, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(60), remaining);
    }

    [Theory]
    // ventana de 60 min; minutos transcurridos -> minutos restantes esperados.
    [InlineData(0, 60)]    // recien sincronizado
    [InlineData(10, 50)]   // encoge a medida que pasa el tiempo
    [InlineData(45, 15)]
    [InlineData(59, 1)]    // casi al final
    public void Remaining_ShrinksAsElapsedGrows(double elapsedMinutes, double expectedMinutes)
    {
        var endsAt = ServerNow.AddMinutes(60);
        var remaining = ExamCountdown.Remaining(
            ServerNow, endsAt, TimeSpan.FromMinutes(elapsedMinutes));
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), remaining);
    }

    [Fact]
    public void Remaining_ElapsedExceedsWindow_ClampsAtZero()
    {
        var endsAt = ServerNow.AddMinutes(60);
        // 61 min transcurridos sobre una ventana de 60 => negativo crudo, clamp a 0.
        var remaining = ExamCountdown.Remaining(
            ServerNow, endsAt, TimeSpan.FromMinutes(61));
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void Remaining_ElapsedExactlyEqualsWindow_IsZero()
    {
        var endsAt = ServerNow.AddMinutes(60);
        // Limite EXACTO: elapsed == ventana => 0 (no negativo, no positivo).
        var remaining = ExamCountdown.Remaining(
            ServerNow, endsAt, TimeSpan.FromMinutes(60));
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void Remaining_EndsAtAlreadyPastAtSync_IsZeroEvenWithNoElapsed()
    {
        // El examen ya termino en el instante del sync (endsAt < serverNow):
        // la ventana cruda es negativa => clamp a 0, sin esperar elapsed.
        var endsAt = ServerNow.AddMinutes(-5);
        var remaining = ExamCountdown.Remaining(ServerNow, endsAt, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void Remaining_EndsAtPastAndElapsedPositive_StaysZero()
    {
        var endsAt = ServerNow.AddMinutes(-5);
        var remaining = ExamCountdown.Remaining(
            ServerNow, endsAt, TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void Remaining_SubMinutePrecision_IsPreserved()
    {
        var endsAt = ServerNow.AddSeconds(90);
        var remaining = ExamCountdown.Remaining(
            ServerNow, endsAt, TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(60), remaining);
    }
}
