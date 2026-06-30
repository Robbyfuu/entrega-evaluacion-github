using System.Diagnostics;
using EntregaEvaluacion.Core;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Mantiene el ANCLA server-authoritative del countdown del examen y entrega el
/// tiempo restante tickeando con un reloj MONOTONICO (<see cref="Stopwatch"/>),
/// nunca con el reloj de pared.
///
/// ANTI-TAMPER: el restante NUNCA se calcula con DateTime.Now/UtcNow. El sync
/// captura (serverNow, endsAt) del servidor mas un timestamp de
/// <see cref="Stopwatch"/> (QueryPerformanceCounter), y el restante se deriva con
/// <see cref="Stopwatch.GetElapsedTime(long)"/>, que es inmune a que el alumno
/// cambie la hora del sistema. El calculo puro vive en
/// <see cref="ExamCountdown.Remaining"/>.
///
/// DEGRADAR sin perder el ancla: <see cref="Sync"/> SOLO debe llamarse cuando el
/// fetch del servidor fue EXITOSO. Si el fetch falla (RPC null/error), el caller
/// simplemente NO llama a Sync: el ancla anterior se conserva y el Stopwatch
/// sigue contando con la verdad (un blip de red NO congela ni reinicia el
/// countdown; solo difiere el RE-anclaje). Sin ancla, o con <c>endsAt</c> null,
/// <see cref="Remaining"/> devuelve null (la slice 4 no muestra countdown).
///
/// THREADING: en WPF el ancla se ESCRIBE en el tick admin (DispatcherTimer, hilo
/// de UI) y se LEE en el timer de 1s del widget (tambien DispatcherTimer, hilo de
/// UI), de modo que en la practica todo ocurre en el mismo hilo. El lock es
/// defensivo: garantiza que la terna (serverNow, endsAt, stamp) se lea/escriba de
/// forma atomica aunque algun consumidor futuro la toque desde otro hilo.
/// </summary>
public sealed class ExamTimerService
{
    private readonly object _lock = new();

    // Ancla del ultimo sync EXITOSO. _hasAnchor distingue "nunca se sincronizo"
    // de un sync con endsAt null (ambos => Remaining null, pero el primero ademas
    // no tiene serverNow/stamp validos).
    private bool _hasAnchor;
    private DateTimeOffset _serverNowAtSync;
    private DateTimeOffset? _endsAt;
    private long _syncStamp; // Stopwatch.GetTimestamp() del instante del sync (monotonico)

    /// <summary>
    /// Re-ancla el countdown con la hora del servidor del ultimo fetch EXITOSO.
    /// Captura el timestamp monotonico ANTES de tomar el lock para que el ancla
    /// refleje el instante de llegada de la respuesta lo mas fielmente posible
    /// (la latencia de adquirir el lock no infla el transcurrido). Si
    /// <paramref name="endsAt"/> es null, queda anclado pero sin countdown
    /// (Remaining null): el examen no tiene fin configurado.
    /// </summary>
    public void Sync(DateTimeOffset serverNow, DateTimeOffset? endsAt)
    {
        var stamp = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            _serverNowAtSync = serverNow;
            _endsAt = endsAt;
            _syncStamp = stamp;
            _hasAnchor = true;
        }
    }

    /// <summary>
    /// Tiempo restante del examen, o null si no hay ancla todavia o el ancla no
    /// tiene fin (endsAt null). El transcurrido se mide con el Stopwatch
    /// monotonico desde el sync; el clamp a cero lo aplica
    /// <see cref="ExamCountdown.Remaining"/>.
    /// </summary>
    public TimeSpan? Remaining
    {
        get
        {
            DateTimeOffset serverNow, endsAt;
            long stamp;
            lock (_lock)
            {
                if (!_hasAnchor || _endsAt is not { } e) return null;
                serverNow = _serverNowAtSync;
                endsAt = e;
                stamp = _syncStamp;
            }
            // GetElapsedTime usa el contador monotonico: NUNCA el reloj de pared.
            return ExamCountdown.Remaining(serverNow, endsAt, Stopwatch.GetElapsedTime(stamp));
        }
    }
}
