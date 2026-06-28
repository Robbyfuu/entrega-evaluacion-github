using EntregaEvaluacion.Core;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Colaborador extraido de MainWindow.CheckUpdateRequestAsync (ENT-7 extraction
/// #3). Update DISPARADO POR EL PROFE desde el panel (NO automatico): el profe
/// setea control.update_requested_at = NOW(); el cliente actualiza UNA vez si ese
/// timestamp es POSTERIOR a su arranque. Asi no pega a la API de GitHub en cada
/// tick (solo cuando el profe lo pide) ni relanza el update en cada arranque por
/// un request viejo.
///
/// Es duenio del estado que antes vivia en MainWindow: el instante de arranque
/// (<c>_processStartUtc</c>, capturado al construirse este watcher en el ctor de
/// MainWindow) y el ultimo request ya procesado (<c>_lastUpdateRequestProcessed</c>,
/// one-shot dedup). La DECISION de que hacer con el request es pura y vive en
/// <see cref="UpdateRequestDecision"/> (testeada con arranque inyectado); aqui
/// solo se lee el control, se aplica la decision y se dispara el update.
///
/// El token de GitHub se pasa por proveedor (<c>Func&lt;string?&gt;</c>) para
/// leerlo en el instante del disparo, igual que el original leia _gh.Token. El
/// UpdateService estatico se mantiene (DIP-1 fuera de alcance).
/// </summary>
public sealed class RemoteUpdateWatcher
{
    private readonly ISupabaseClient _sb;
    private readonly ILogSink _log;
    private readonly Func<string?> _tokenProvider;

    // Arranque del cliente (UTC). Se captura al construir el watcher, que
    // MainWindow crea en su ctor: equivale al field initializer original
    // (_processStartUtc = DateTime.UtcNow), que tambien corria al construir la
    // ventana. Solo se actua sobre requests POSTERIORES a este instante.
    private readonly DateTime _processStartUtc = DateTime.UtcNow;

    // Ultimo update_requested_at ya procesado (one-shot dedup por valor crudo).
    private string? _lastUpdateRequestProcessed;

    public RemoteUpdateWatcher(ISupabaseClient sb, ILogSink log, Func<string?> tokenProvider)
    {
        _sb = sb;
        _log = log;
        _tokenProvider = tokenProvider;
    }

    /// <summary>
    /// Lee el control (Supabase, barato; ya se hace cada tick), decide y, si
    /// corresponde, dispara el update (GitHub solo se toca al disparar). El sello
    /// _lastUpdateRequestProcessed se actualiza tanto al "marcar visto" como al
    /// "disparar"; en Ignore queda igual (la decision devuelve el valor previo).
    /// </summary>
    public async Task CheckAsync()
    {
        var ctl = await _sb.GetControlAsync();
        var outcome = UpdateRequestDecision.Decide(
            ctl?.UpdateRequestedAt, _lastUpdateRequestProcessed, _processStartUtc);

        if (outcome.Action == UpdateRequestAction.Ignore) return;

        // MarkSeenOnly y Trigger comparten el sellado; Ignore ya retorno arriba.
        _lastUpdateRequestProcessed = outcome.NewLastProcessed;
        if (outcome.Action != UpdateRequestAction.Trigger) return;

        _log.Log("[update] el profesor pidio actualizar. Buscando version nueva...");
        // reinicia si hay update
        await UpdateService.CheckAndApplyAsync(msg => _log.Log(msg), _tokenProvider());
    }
}
