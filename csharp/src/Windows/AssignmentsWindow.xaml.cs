using System.Windows;
using System.Windows.Controls;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Lista de tareas de GitHub Classroom con estado de aceptacion. Cada item se
/// marca como "Pendiente" (badge naranja + boton Aceptar) o "Aceptada" (badge
/// verde + link al repo). El cruce de estado (repo existente / registro en BD)
/// lo calcula MainWindow y se pasa ya resuelto como AssignmentStatus.
/// </summary>
public partial class AssignmentsWindow : Window
{
    // Aceptar: recibe el status completo (para registrar la aceptacion + abrir Classroom).
    private readonly Action<AssignmentStatus> _onAccept;
    // Abrir una URL cualquiera (repo) en el navegador embebido.
    private readonly Action<string> _openUrl;

    public AssignmentsWindow(
        List<AssignmentStatus> statuses,
        Action<AssignmentStatus> onAccept,
        Action<string> openUrl)
    {
        InitializeComponent();
        _onAccept = onAccept;
        _openUrl = openUrl;
        ItemsHost.ItemsSource = statuses;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AssignmentStatus status })
            _onAccept(status);
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.HyperlinkButton { Tag: string url } && !string.IsNullOrEmpty(url))
            _openUrl(url);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
