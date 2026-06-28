namespace EntregaEvaluacion.Core;

/// <summary>
/// Decisiones PURAS de la sonda de red (deteccion de contacto a Copilot),
/// extraidas de MainWindow.CheckNetworkProbeAsync. Sin WPF, sin I/O y sin leer
/// el reloj: el caller inyecta <c>now</c> para que el gating sea determinista y
/// testeable. El caller (NetworkProbeReporter) conserva el estado mutable
/// (ultimo probe + mapa de hits) y corre la sonda real.
///
/// Preserva EXACTO los limites del original, que saltaba con <c>&lt; ventana</c>:
///   - probe: corre si paso AL MENOS la ventana (30s) desde la ultima corrida.
///   - reporte: re-reporta una clave (host+source) si NUNCA se vio o si paso AL
///     MENOS la ventana (5 min) desde su ultimo reporte.
/// En ambos casos el instante EXACTO de la ventana SI procede (limite inclusivo).
/// </summary>
public static class ProbeThrottle
{
    /// <summary>
    /// True si corresponde correr la sonda: paso al menos <paramref name="window"/>
    /// desde <paramref name="lastProbeUtc"/>. Equivale al original
    /// <c>(now - last).TotalSeconds &lt; 30</c> que SALTABA (aqui negado).
    /// </summary>
    public static bool ShouldProbe(DateTime lastProbeUtc, DateTime now, TimeSpan window)
        => now - lastProbeUtc >= window;

    /// <summary>
    /// True si corresponde reportar la clave <paramref name="key"/>: no esta en el
    /// mapa, o paso al menos <paramref name="window"/> desde su ultimo reporte.
    /// Equivale al original <c>map.TryGetValue(key, out last) &amp;&amp;
    /// (now - last).TotalMinutes &lt; 5</c> que SALTABA (aqui negado). La dedup es
    /// POR clave: un hit reciente de otra clave no afecta esta.
    /// </summary>
    public static bool ShouldReportHit(
        IReadOnlyDictionary<string, DateTime> reportedHits,
        string key, DateTime now, TimeSpan window)
    {
        if (reportedHits.TryGetValue(key, out var last) && now - last < window)
            return false;
        return true;
    }
}
