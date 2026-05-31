using System.Windows;
using System.Windows.Controls;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Lista de tareas de GitHub Classroom. Cada item abre su ClassroomUrl via el
/// callback recibido (delega en MainWindow.OpenUrl). Reemplaza al Form inline
/// de MainForm.ShowAssignmentsDialog conservando su comportamiento.
/// </summary>
public partial class AssignmentsWindow : Window
{
    private readonly Action<string> _openUrl;

    public AssignmentsWindow(List<Assignment> assignments, Action<string> openUrl)
    {
        InitializeComponent();
        _openUrl = openUrl;
        ItemsHost.ItemsSource = assignments;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrEmpty(url))
            _openUrl(url);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
