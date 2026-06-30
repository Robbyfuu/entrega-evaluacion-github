using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using EntregaEvaluacion.Core;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Widget flotante del countdown (ENT-31 slice 4). Aparece arriba a la derecha
/// cuando el alumno MINIMIZA la ventana principal durante una evaluacion y
/// muestra el tiempo restante (HH:MM:SS) mas un boton "Ir a la entrega" que
/// restaura la ventana. Es siempre-encima (Topmost) pero NUNCA debe tapar la
/// pantalla roja: el MainWindow lo oculta antes de cada lockdown y este widget
/// nunca se muestra solo (la visibilidad la decide MainWindow.UpdateWidgetVisibility).
///
/// SOLO LEE: el restante viene de <see cref="Services.ExamTimerService"/> (slice
/// 3, ancla server-authoritative + Stopwatch monotonico) inyectado como
/// <c>Func&lt;TimeSpan?&gt;</c>; el widget no hace red ni aritmetica de tiempo,
/// solo formatea con <see cref="ExamCountdown.Format"/>. En T=0 pinta el reloj en
/// ROJO de advertencia, SIN ninguna accion automatica.
///
/// SIN Owner: una ventana "owned" se auto-oculta cuando su owner se minimiza,
/// justo el instante en que necesitamos verla. Por eso NO se setea Owner y el
/// ciclo de vida lo maneja MainWindow (Show/Hide/PositionTopRight + Close al
/// cerrar la app, ya que sin Owner no se cierra solo).
/// </summary>
public partial class WidgetWindow : Window
{
    // Separacion respecto del borde del area de trabajo (px DIU).
    private const double EdgeMargin = 16;

    private readonly Func<TimeSpan?> _remaining;
    private readonly Action _onRestore;
    private readonly DispatcherTimer _timer;

    private readonly Brush _normalBrush;
    private readonly Brush _dangerBrush;

    /// <param name="remaining">
    /// Fuente del restante (normalmente <c>() =&gt; examTimer.Remaining</c>).
    /// null = sin ancla o sin fin configurado => no se muestra nada util.
    /// </param>
    /// <param name="onRestore">
    /// Callback que restaura la ventana principal (la inyecta MainWindow; el
    /// widget NO manipula MainWindow directamente).
    /// </param>
    public WidgetWindow(Func<TimeSpan?> remaining, Action onRestore)
    {
        _remaining = remaining;
        _onRestore = onRestore;

        InitializeComponent();

        // Brushes resueltos una vez (viven en los recursos de App via merged dict).
        _normalBrush = (Brush)FindResource("TextBrush");
        _dangerBrush = (Brush)FindResource("DangerBrush");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();

        Loaded += (_, _) =>
        {
            PositionTopRight();
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// Reposiciona el widget arriba a la derecha respetando la barra de tareas
    /// (<see cref="SystemParameters.WorkArea"/>). MainWindow lo llama antes de cada
    /// Show por si cambio el area de trabajo (resolucion / monitor).
    /// </summary>
    public void PositionTopRight()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - EdgeMargin;
        Top = wa.Top + EdgeMargin;
    }

    // Lee SOLO el restante y lo pinta. Sin red, sin aritmetica de tiempo.
    private void Refresh()
    {
        if (_remaining() is not { } r)
        {
            // Sin ancla todavia o examen sin fin configurado: nada que contar.
            TimeText.Text = "--:--:--";
            TimeText.Foreground = _normalBrush;
            return;
        }

        TimeText.Text = ExamCountdown.Format(r);
        // T=0 => rojo de advertencia. NO hay auto-submit ni auto-lock: visual only.
        TimeText.Foreground = r <= TimeSpan.Zero ? _dangerBrush : _normalBrush;
    }

    private void GoButton_Click(object sender, RoutedEventArgs e) => _onRestore();
}
