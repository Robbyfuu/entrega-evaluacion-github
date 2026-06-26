using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Pantalla roja de bloqueo (kiosk) en WPF. Sin boton X, Topmost, fullscreen.
/// Se libera con clave del profesor o (si es remoto) cuando el profe libera
/// desde el panel. Reemplaza al antiguo CheatAlertForm conservando su logica.
/// </summary>
public partial class CheatWindow : Window
{
    private readonly bool _remoteSource;
    private readonly Func<bool>? _checkStillLocked;
    private readonly Action? _onHeartbeat;
    private DispatcherTimer? _releaseTimer;

    // Mientras no se libere correctamente, no se permite cerrar la ventana.
    private bool _allowClose;

    public CheatWindow(
        string repoName,
        int filesCount,
        string[] filesSample,
        bool isPersistent = false,
        bool remoteSource = false,
        Func<bool>? checkStillLocked = null,
        Action? onHeartbeat = null)
    {
        InitializeComponent();

        _remoteSource = remoteSource;
        _checkStillLocked = checkStillLocked;
        _onHeartbeat = onHeartbeat;

        var sample = string.Join(", ", filesSample);
        var msg =
            $"Repositorio: '{repoName}' contiene {filesCount} archivo(s) NO permitidos:\n\n" +
            $"  {sample}\n\n" +
            "Una evaluacion en blanco solo deberia tener README, LICENSE o .gitignore.\n\n" +
            "Este intento fue REGISTRADO. El profesor sera notificado.\n\n" +
            "Esta ventana esta BLOQUEADA y el Administrador de Tareas tambien.\n" +
            "Solo el profesor puede desbloquear este equipo con su clave.";
        if (isPersistent)
            msg += "\n\n[Detectado en sesion anterior. El bloqueo persistira en cada reinicio.]";
        MsgText.Text = msg;

        // Bloquear teclas problematicas (Alt+F4, Alt+Tab, Esc)
        PreviewKeyDown += CheatWindow_PreviewKeyDown;

        // Bloquear cierre mientras no haya liberacion valida
        Closing += (_, ev) => { if (!_allowClose) ev.Cancel = true; };

        Loaded += CheatWindow_Loaded;
    }

    private void CheatWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Topmost = true;
        Activate();

        // Timer mientras la pantalla roja esta arriba: (1) late el heartbeat para
        // que el panel siga viendo a ESTE PC como online + bloqueado (el AdminTick
        // queda bloqueado tras el ShowDialog modal), y (2) si es remoto, consulta
        // si ya se libero para cerrarse.
        if (_onHeartbeat != null || (_remoteSource && _checkStillLocked != null))
        {
            _releaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10000) };
            _releaseTimer.Tick += (_, _) =>
            {
                try { _onHeartbeat?.Invoke(); } catch { }
                try
                {
                    if (_checkStillLocked != null && !_checkStillLocked())
                    {
                        _releaseTimer?.Stop();
                        LockdownService.Release();
                        CloseUnlocked();
                    }
                }
                catch { }
            };
            _releaseTimer.Start();
        }
    }

    private void CheatWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool altDown = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        // En WPF Alt+tecla llega como SystemKey
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if ((altDown && key == Key.F4) ||
            (altDown && key == Key.Tab) ||
            key == Key.Escape)
        {
            e.Handled = true;
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e) => PromptPassword();

    private void CloseUnlocked()
    {
        _allowClose = true;
        _releaseTimer?.Stop();
        Close();
    }

    private void PromptPassword()
    {
        var dlg = new PasswordPromptWindow { Owner = this };
        var ok = dlg.ShowDialog();
        if (ok == true)
        {
            // La clave ya fue validada y LockdownService.Release() ejecutado
            // dentro del dialogo. Solo cerramos.
            CloseUnlocked();
        }
    }
}
