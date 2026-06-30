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

    /// <summary>
    /// Formatea un restante como "HH:MM:SS" con cero a la izquierda. Las HORAS son
    /// el TOTAL (no el componente 0-23): una ventana de 100h se muestra "100:00:00",
    /// no "04:00:00". Los segundos sub-segundo se truncan (piso), de modo que el
    /// reloj cae a "00:00:00" recien al llegar a cero. Un restante negativo (no
    /// deberia ocurrir: <see cref="Remaining"/> ya clampa) se trata como cero.
    ///
    /// PURO: solo da forma a un <see cref="TimeSpan"/> ya calculado; no hace
    /// aritmetica de tiempo ni lee reloj alguno. El widget (slice 4) lo usa para
    /// pintar sin replicar logica de tiempo.
    /// </summary>
    public static string Format(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        int totalHours = (int)remaining.TotalHours;
        return $"{totalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }
}
