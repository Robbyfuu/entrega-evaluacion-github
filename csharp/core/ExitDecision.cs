namespace EntregaEvaluacion.Core;

/// <summary>
/// Decision PURA del guard de cierre de la ventana principal, extraida de
/// MainWindow.OnClosing. Sin WPF, sin I/O y sin reloj: el caller (ExitGuard)
/// conserva el estado mutable (_allowExit) y ejecuta los efectos (toast, reporte,
/// prompt de clave, shutdown). Aqui solo vive el gate booleano.
///
/// Preserva EXACTO el gate del original, que permitia el cierre con un return
/// temprano: <c>if (_allowExit || e.Cancel || UpdateService.IsApplying) return;</c>.
/// NO existe una condicion de "en evaluacion": el bloqueo es incondicional salvo
/// estas tres banderas (agregar un gate de estado de examen seria una regresion
/// de integridad).
/// </summary>
public static class ExitDecision
{
    /// <summary>
    /// True si corresponde PERMITIR el cierre (no bloquear). Equivale al gate del
    /// original: se permite si ya se autorizo con clave
    /// (<paramref name="allowExit"/>), si otro handler ya cancelo el cierre
    /// (<paramref name="alreadyCancelled"/>) o si hay un update aplicandose
    /// (<paramref name="isUpdating"/>, Velopack reinicia la app). Si los tres son
    /// false, el cierre se BLOQUEA.
    /// </summary>
    public static bool ShouldAllowClose(bool allowExit, bool alreadyCancelled, bool isUpdating)
        => allowExit || alreadyCancelled || isUpdating;
}
