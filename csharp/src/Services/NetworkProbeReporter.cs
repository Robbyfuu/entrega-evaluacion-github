using EntregaEvaluacion.Core;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Colaborador extraido de MainWindow.CheckNetworkProbeAsync (ENT-7 extraction
/// #3). Corre la sonda de red (deteccion de contacto a endpoints de Copilot) con
/// throttle + dedup y reporta cada hallazgo NUEVO como evento ai_endpoint_contacted.
/// Es EVIDENCIA para revision, NUNCA veredicto ni lockdown automatico.
///
/// Es duenio del estado de gating que antes vivia en MainWindow: el ultimo
/// instante de sonda (<c>_lastNetProbeUtc</c>, throttle 30s) y el mapa de hits ya
/// reportados (<c>_reportedAiHits</c>, dedup 5 min por host+source). Las DECISIONES
/// de gating son puras y viven en <see cref="ProbeThrottle"/> (testeadas con reloj
/// inyectado); aqui solo se conserva el estado, se corre la sonda real (llamada
/// estatica a <see cref="NetworkProbeService"/>, DIP-1 fuera de alcance) y se hacen
/// los reportes.
///
/// MUST NEVER THROW: preserva el best-effort del original (la sonda ya nunca
/// lanza; ademas se envuelve la corrida y cada reporte en try/catch que solo
/// loguea). El caller pasa el reloj (<c>now</c>) para gating determinista.
/// </summary>
public sealed class NetworkProbeReporter
{
    // Throttle entre corridas de la sonda y dedup por (host+source). Mismos
    // valores que el original (30s / 5 min).
    private static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HitDedupWindow = TimeSpan.FromMinutes(5);

    private readonly ISupabaseClient _sb;
    private readonly ILogSink _log;

    // Ultima corrida de la sonda (UTC). MinValue => nunca corrio: la primera
    // sonda pasa el throttle.
    private DateTime _lastNetProbeUtc = DateTime.MinValue;

    // Ultimo reporte por clave "{Host}|{Source}" (UTC). Ordinal/case-sensitive,
    // igual que el Dictionary&lt;string,DateTime&gt; por defecto del original.
    private readonly Dictionary<string, DateTime> _reportedAiHits = new();

    public NetworkProbeReporter(ISupabaseClient sb, ILogSink log)
    {
        _sb = sb;
        _log = log;
    }

    /// <summary>
    /// Aplica el throttle de 30s; si procede, corre la sonda y reporta cada
    /// hallazgo no visto en los ultimos 5 min (por host+source). El caller pasa la
    /// identidad del alumno (ya resuelta, _user != null garantizado por el caller)
    /// y el reloj. Preserva EXACTO los argumentos del ReportStudentActivityAsync
    /// original: host -> repoName, detail -> repoUrl.
    /// </summary>
    public async Task CheckAsync(
        string username, string? email, string pcName,
        string? section, long? sectionId, DateTime now)
    {
        // Throttle: la sonda corre a lo sumo cada 30s. El sello se actualiza ANTES
        // de correr (igual que el original), asi una corrida lenta no abre la
        // ventana al proximo tick.
        if (!ProbeThrottle.ShouldProbe(_lastNetProbeUtc, now, ProbeWindow)) return;
        _lastNetProbeUtc = now;

        List<NetworkProbeService.Finding> findings;
        try { findings = await Task.Run(() => NetworkProbeService.Probe()); }
        catch (Exception ex) { _log.Log($"[NetProbe] fallo: {ex.Message}"); return; }

        foreach (var f in findings)
        {
            var key = $"{f.Host}|{f.Source}";
            // Dedup por clave: re-reporta solo si nunca se vio o pasaron 5 min.
            if (!ProbeThrottle.ShouldReportHit(_reportedAiHits, key, now, HitDedupWindow)) continue;
            // Se sella el hit ANTES de reportar (igual que el original): aunque el
            // reporte falle, la clave queda dedupeada hasta que venza la ventana.
            _reportedAiHits[key] = now;
            _log.Log($"[NetProbe] contacto Copilot: {f.Host} ({f.Detail})");
            try
            {
                await _sb.ReportStudentActivityAsync(
                    "ai_endpoint_contacted", username, email, pcName,
                    section, f.Host, f.Detail, sectionId);
            }
            catch (Exception ex) { _log.Log($"[NetProbe] reporte fallo: {ex.Message}"); }
        }
    }
}
