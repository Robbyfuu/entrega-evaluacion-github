namespace EntregaEvaluacion.Core;

/// <summary>
/// Calculador PURO del tiempo restante del examen (nucleo anti-tamper). Sin WPF,
/// sin I/O y, sobre todo, SIN leer el reloj de pared (DateTime.Now/UtcNow): el
/// caller inyecta el ancla del servidor y el tiempo transcurrido para que el
/// resultado sea determinista y testeable.
///
/// INVARIANTE ANTI-TAMPER: el <paramref name="elapsedSinceSync"/> debe provenir
/// de un reloj MONOTONICO (Stopwatch / QueryPerformanceCounter), NUNCA del reloj
/// del sistema. Asi un alumno que cambie la hora del equipo no puede alargar ni
/// acortar su examen: el ancla (serverNow + endsAt) viene del servidor y el
/// transcurrido lo mide un contador inmune a los saltos del reloj de pared.
///
/// Regla: remaining = (endsAt - serverNowAtSync) - elapsedSinceSync, SIEMPRE
/// clampeado a <see cref="TimeSpan.Zero"/> (nunca negativo).
/// </summary>
public static class ExamCountdown
{
    /// <summary>
    /// Tiempo restante del examen en el instante <c>serverNowAtSync +
    /// elapsedSinceSync</c>, clampeado a cero.
    /// </summary>
    /// <param name="serverNowAtSync">Hora del servidor capturada en el ultimo sync.</param>
    /// <param name="endsAt">Instante absoluto (del servidor) en que termina el examen.</param>
    /// <param name="elapsedSinceSync">
    /// Tiempo transcurrido desde el sync, medido con un reloj MONOTONICO (no el
    /// reloj de pared). Normalmente <see cref="System.Diagnostics.Stopwatch"/>.
    /// </param>
    /// <returns>Restante &gt;= <see cref="TimeSpan.Zero"/> (nunca negativo).</returns>
    public static TimeSpan Remaining(
        DateTimeOffset serverNowAtSync,
        DateTimeOffset endsAt,
        TimeSpan elapsedSinceSync)
    {
        var remaining = (endsAt - serverNowAtSync) - elapsedSinceSync;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
