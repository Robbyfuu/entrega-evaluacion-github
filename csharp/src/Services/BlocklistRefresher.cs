namespace EntregaEvaluacion.Services;

/// <summary>
/// Colaborador extraido de MainWindow.RefreshBlocklistAsync. Envuelve el fetch
/// del blocklist efectivo de procesos (global union seccion) desde la tabla
/// suspicious_processes via ISupabaseClient.
///
/// Preserva EXACTO la semantica de null del original: <see cref="RefreshAsync"/>
/// devuelve null cuando el fetch falla o no hay datos validos (GetBlocklistAsync
/// devuelve null en ambos casos), y el caller interpreta ese null como "fallback
/// a Config.SuspiciousProcesses" (NO como lista vacia). El caller guarda el
/// resultado tal cual en su campo _blocklist.
/// </summary>
public sealed class BlocklistRefresher
{
    private readonly ISupabaseClient _sb;

    public BlocklistRefresher(ISupabaseClient sb)
    {
        _sb = sb;
    }

    /// <summary>
    /// Trae el blocklist efectivo. section_id (multi-evaluacion) es preferido;
    /// cae a section TEXT si es null (forward-compat con clientes viejos). null
    /// preservado = fallback a Config.
    /// </summary>
    public async Task<IReadOnlySet<string>?> RefreshAsync(string? section, long? sectionId)
        => await _sb.GetBlocklistAsync(section, sectionId);
}
