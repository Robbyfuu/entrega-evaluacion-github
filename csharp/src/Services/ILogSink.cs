namespace EntregaEvaluacion.Services;

/// <summary>
/// Sumidero del log de diagnostico. Espeja la superficie de Log(...) de la
/// vista: agrega la linea al historial en memoria y a la ventana de detalle en
/// vivo. Permite que servicios extraidos dependan de esta abstraccion en lugar
/// de acoplarse a la UI.
/// </summary>
public interface ILogSink
{
    // Registra un mensaje de diagnostico (historial en memoria + ventana de detalle).
    void Log(string msg);
}
