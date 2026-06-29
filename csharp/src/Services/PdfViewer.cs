namespace EntregaEvaluacion.Services;

/// <summary>
/// Colaborador de I/O (ENT-7 extraction #4) que envuelve el flujo de
/// descargar+abrir el PDF de enunciado. Conserva las llamadas ESTATICAS a
/// <see cref="ExamPdfService"/> (DIP-1 fuera de alcance) y NO toca WPF: el caller
/// (MainWindow.ViewPdfButton_Click) decide el feedback de UI (toast, habilitar/
/// deshabilitar el boton) segun el resultado.
///
/// No hay logica de decision pura que extraer a EntregaEvaluacion.Core: la unica
/// regla ("no hay PDF si la ruta esta vacia") es un guard trivial de I/O, no un
/// algebra de estados; forzar un helper de core + su test seria sobre-ingenieria.
/// </summary>
public sealed class PdfViewer
{
    /// <summary>
    /// Descarga y abre el PDF de enunciado en la ruta de storage dada. Devuelve
    /// true si se abrio; false si la ruta estaba vacia o la descarga/apertura
    /// fallo. Best-effort: no lanza (igual que <see cref="ExamPdfService"/>).
    /// </summary>
    public async Task<bool> TryOpenAsync(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) return false;
        var local = await ExamPdfService.DownloadAndOpenAsync(storagePath);
        return local != null;
    }
}
