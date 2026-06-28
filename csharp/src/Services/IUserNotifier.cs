using EntregaEvaluacion.Core;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Feedback transitorio al usuario. Espeja la superficie de Status(...) y
/// ShowToast(...) de la vista: barra de estado y toast auto-ocultable. Permite
/// que servicios extraidos dependan de esta abstraccion en lugar de acoplarse a
/// la UI.
/// </summary>
public interface IUserNotifier
{
    // Actualiza la barra de estado con el ultimo mensaje.
    void Status(string msg);

    // Muestra un toast transitorio; el acento depende del ToastKind.
    void ShowToast(string msg, ToastKind kind);
}
