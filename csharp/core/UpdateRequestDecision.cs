namespace EntregaEvaluacion.Core;

/// <summary>
/// Que hacer con un <c>update_requested_at</c> leido del control. Las tres
/// salidas del original (MainWindow.CheckUpdateRequestAsync), modeladas explicito
/// porque "marcar visto sin actualizar" es una transicion observable distinta de
/// "no hacer nada": un bool no alcanza para preservarla.
/// </summary>
public enum UpdateRequestAction
{
    /// <summary>Sin cambios: raw vacio, ya procesado, o no parseable.</summary>
    Ignore,

    /// <summary>
    /// Marcar como visto pero NO actualizar: el request es anterior (o igual) al
    /// arranque, asi un request viejo no relanza el update en cada arranque.
    /// </summary>
    MarkSeenOnly,

    /// <summary>Marcar como procesado y disparar el update (one-shot).</summary>
    Trigger,
}

/// <summary>
/// Resultado de <see cref="UpdateRequestDecision.Decide"/>: la accion a tomar y
/// el nuevo valor de "ultimo request procesado". En <see cref="UpdateRequestAction.Ignore"/>
/// el valor queda igual al previo (el caller puede asignarlo sin condicionar).
/// </summary>
public readonly record struct UpdateRequestOutcome(
    UpdateRequestAction Action, string? NewLastProcessed);

/// <summary>
/// Decision PURA del update remoto disparado por el profe, extraida de
/// MainWindow.CheckUpdateRequestAsync. No lee el reloj: solo compara el timestamp
/// del request contra el instante de arranque capturado (<paramref name="processStartUtc"/>),
/// por eso no recibe <c>now</c> (seria un parametro muerto). Preserva EXACTO el
/// orden de chequeos y el limite inclusivo <c>&lt;=</c> del original.
/// </summary>
public static class UpdateRequestDecision
{
    public static UpdateRequestOutcome Decide(
        string? updateRequestedAt, string? lastProcessed, DateTime processStartUtc)
    {
        // raw vacio: no hay request -> sin cambios.
        if (string.IsNullOrEmpty(updateRequestedAt))
            return new UpdateRequestOutcome(UpdateRequestAction.Ignore, lastProcessed);

        // Ya procesado (one-shot dedup por valor crudo): sin cambios.
        if (updateRequestedAt == lastProcessed)
            return new UpdateRequestOutcome(UpdateRequestAction.Ignore, lastProcessed);

        // No parseable: sin cambios (no se marca como visto, igual que el original).
        if (!DateTimeOffset.TryParse(updateRequestedAt, out var reqDto))
            return new UpdateRequestOutcome(UpdateRequestAction.Ignore, lastProcessed);

        // Anterior o IGUAL al arranque (limite inclusivo <=): marcar visto, NO disparar.
        if (reqDto.UtcDateTime <= processStartUtc)
            return new UpdateRequestOutcome(UpdateRequestAction.MarkSeenOnly, updateRequestedAt);

        // Posterior al arranque y no procesado: marcar y disparar.
        return new UpdateRequestOutcome(UpdateRequestAction.Trigger, updateRequestedAt);
    }
}
