using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests de la decision PURA del update remoto, extraida de
/// MainWindow.CheckUpdateRequestAsync hacia <see cref="UpdateRequestDecision"/>.
///
/// Congela EXACTO las tres salidas del original:
///   - Ignore       : raw vacio, ya procesado, o no parseable => sin cambios.
///   - MarkSeenOnly : request ANTERIOR (o igual) al arranque => marcar visto, NO
///                    actualizar (un request viejo no relanza updates al arrancar).
///   - Trigger      : request POSTERIOR al arranque y no procesado => marcar y
///                    disparar el update UNA sola vez (one-shot dedup por raw).
///
/// La decision NO lee el reloj: solo compara el timestamp del request contra el
/// instante de arranque capturado. Por eso no recibe `now` (seria un parametro
/// muerto): inyectar el arranque ya la hace determinista.
/// </summary>
public class UpdateRequestDecisionTests
{
    // Arranque del cliente (UTC). Los requests se comparan contra este instante.
    private static readonly DateTime ProcessStart = new(2026, 6, 28, 10, 0, 0, DateTimeKind.Utc);
    private const string Before = "2026-06-28T09:59:59Z"; // anterior al arranque
    private const string AtStart = "2026-06-28T10:00:00Z"; // EXACTO al arranque
    private const string After = "2026-06-28T10:00:01Z";  // posterior al arranque

    // ===== Ignore: sin cambios de estado =====

    [Fact]
    public void NullRaw_IsIgnored_KeepsLastProcessed()
    {
        var d = UpdateRequestDecision.Decide(null, "marca-previa", ProcessStart);
        Assert.Equal(UpdateRequestAction.Ignore, d.Action);
        Assert.Equal("marca-previa", d.NewLastProcessed);
    }

    [Fact]
    public void EmptyRaw_IsIgnored_KeepsLastProcessed()
    {
        var d = UpdateRequestDecision.Decide("", null, ProcessStart);
        Assert.Equal(UpdateRequestAction.Ignore, d.Action);
        Assert.Null(d.NewLastProcessed);
    }

    [Fact]
    public void AlreadyProcessed_IsIgnored_EvenWhenAfterStart()
    {
        // One-shot: si raw ya es la marca procesada, no se vuelve a disparar
        // aunque sea posterior al arranque.
        var d = UpdateRequestDecision.Decide(After, After, ProcessStart);
        Assert.Equal(UpdateRequestAction.Ignore, d.Action);
        Assert.Equal(After, d.NewLastProcessed);
    }

    [Fact]
    public void UnparseableRaw_IsIgnored_KeepsLastProcessed()
    {
        var d = UpdateRequestDecision.Decide("no-es-fecha", "marca-previa", ProcessStart);
        Assert.Equal(UpdateRequestAction.Ignore, d.Action);
        Assert.Equal("marca-previa", d.NewLastProcessed);
    }

    // ===== MarkSeenOnly: request <= arranque =====

    [Fact]
    public void RequestBeforeStart_MarksSeen_DoesNotTrigger()
    {
        var d = UpdateRequestDecision.Decide(Before, null, ProcessStart);
        Assert.Equal(UpdateRequestAction.MarkSeenOnly, d.Action);
        Assert.Equal(Before, d.NewLastProcessed);
    }

    [Fact]
    public void RequestExactlyAtStart_MarksSeen_DoesNotTrigger()
    {
        // El original usa `<= _processStartUtc`: el instante EXACTO del arranque
        // NO dispara (se marca como visto).
        var d = UpdateRequestDecision.Decide(AtStart, null, ProcessStart);
        Assert.Equal(UpdateRequestAction.MarkSeenOnly, d.Action);
        Assert.Equal(AtStart, d.NewLastProcessed);
    }

    // ===== Trigger: request posterior al arranque y no procesado =====

    [Fact]
    public void RequestAfterStart_NotProcessed_Triggers()
    {
        var d = UpdateRequestDecision.Decide(After, null, ProcessStart);
        Assert.Equal(UpdateRequestAction.Trigger, d.Action);
        Assert.Equal(After, d.NewLastProcessed);
    }

    [Fact]
    public void RequestAfterStart_DifferentFromPrevious_Triggers()
    {
        // Un request nuevo (distinto al ya procesado) y posterior al arranque
        // vuelve a disparar.
        var d = UpdateRequestDecision.Decide(After, Before, ProcessStart);
        Assert.Equal(UpdateRequestAction.Trigger, d.Action);
        Assert.Equal(After, d.NewLastProcessed);
    }

    [Fact]
    public void TriggerThenSameRaw_BecomesIgnore_OneShot()
    {
        // Secuencia real: primer tick dispara; segundo tick con el MISMO raw
        // (ya guardado como lastProcessed) se ignora.
        var first = UpdateRequestDecision.Decide(After, null, ProcessStart);
        Assert.Equal(UpdateRequestAction.Trigger, first.Action);

        var second = UpdateRequestDecision.Decide(After, first.NewLastProcessed, ProcessStart);
        Assert.Equal(UpdateRequestAction.Ignore, second.Action);
        Assert.Equal(After, second.NewLastProcessed);
    }

    [Fact]
    public void RequestWithOffset_NormalizedToUtcForComparison()
    {
        // El original compara reqDto.UtcDateTime: un offset distinto de Z se
        // normaliza a UTC antes de comparar contra el arranque.
        // 2026-06-28T12:00:00+02:00 == 10:00:00Z == arranque EXACTO => MarkSeenOnly.
        var d = UpdateRequestDecision.Decide("2026-06-28T12:00:00+02:00", null, ProcessStart);
        Assert.Equal(UpdateRequestAction.MarkSeenOnly, d.Action);
    }
}
