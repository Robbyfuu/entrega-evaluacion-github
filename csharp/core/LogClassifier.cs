namespace EntregaEvaluacion.Core;

/// <summary>
/// Clasifica mensajes de log para decidir si merecen un toast y de que tipo.
/// Logica pura, sin UI.
/// </summary>
public static class LogClassifier
{
    // Decide si un mensaje de Log() merece toast y de que tipo.
    // Devuelve null para mensajes rutinarios (solo barra de estado).
    public static ToastKind? Classify(string? msg)
    {
        // Mensaje nulo/vacio -> sin toast (degradar, no romper la UI).
        if (string.IsNullOrEmpty(msg)) return null;
        var m = msg.ToLowerInvariant();
        if (m.Contains("error") || m.Contains("fallo") || m.Contains("trampa") || m.StartsWith("no se"))
            return ToastKind.Error;
        if (m.StartsWith("ok ") || m.Contains("completada") || m.Contains("creado") ||
            m.Contains("clonado") || m.Contains("sesion iniciada") || m.Contains("sesion cerrada") ||
            m.Contains("limpio"))
            return ToastKind.Success;
        if (m.StartsWith("[admin]"))
            return ToastKind.Info;
        return null;
    }
}
