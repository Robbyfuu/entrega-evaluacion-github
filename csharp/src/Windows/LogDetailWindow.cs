using System.Windows;
using System.Windows.Media;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Ventana de detalle del log (mono, fondo oscuro "Consola Ops"). Util para
/// el profesor en debug. Se crea desde el link "Ver detalles" y muestra el
/// historial completo guardado en memoria por MainWindow.
/// </summary>
public sealed class LogDetailWindow : Window
{
    private readonly System.Windows.Controls.TextBox _box;

    public LogDetailWindow()
    {
        Title = "Detalle del registro";
        Width = 720;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12),
            FontSize = 13,
        };

        // Colores Consola Ops (oscuros), independientes del tema claro.
        _box.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D0E14"));
        _box.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ADE80"));
        _box.FontFamily = new System.Windows.Media.FontFamily("Consolas, Cascadia Code");

        Background = _box.Background;
        Content = _box;
    }

    public void SetText(string text)
    {
        _box.Text = text;
        _box.CaretIndex = _box.Text.Length;
        _box.ScrollToEnd();
    }

    public void AppendLine(string line)
    {
        _box.AppendText(line + "\r\n");
        _box.CaretIndex = _box.Text.Length;
        _box.ScrollToEnd();
    }
}
