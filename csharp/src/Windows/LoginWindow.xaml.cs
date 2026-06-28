using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using EntregaEvaluacion.Models;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Login via device flow en WPF. Muestra codigo grande + URL, hace polling del
/// token y cierra con DialogResult=true cuando el alumno autoriza. Reemplaza al
/// antiguo LoginDialog conservando su logica de flow y polling.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly IGitHubService _gh;
    private DispatcherTimer? _pollTimer;
    private string _deviceCode = "";
    private DateTime _expiresAt;
    private string _verifyUri = "";
    // Guard anti-reentrancia: con timeout 30s e intervalo 5s pueden solaparse
    // varios PollAsync sobre el mismo device_code. El flag asegura que solo uno
    // este en vuelo a la vez.
    private bool _isPolling;

    public LoginWindow(IGitHubService gh)
    {
        InitializeComponent();
        _gh = gh;
        Loaded += async (_, _) => await StartFlowAsync();
    }

    private async Task StartFlowAsync()
    {
        var dc = await _gh.RequestDeviceCodeAsync();
        if (dc == null)
        {
            StatusText.Text = "Error contactando GitHub. Revisa tu internet.";
            StatusText.Foreground = Brushes.Red;
            Progress.IsIndeterminate = false;
            return;
        }

        _deviceCode = dc.DeviceCode;
        _verifyUri = dc.VerificationUri;
        _expiresAt = DateTime.UtcNow.AddSeconds(dc.ExpiresIn);
        CodeText.Text = dc.UserCode;
        StatusText.Text = "Esperando que ingreses el codigo...";

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(dc.Interval, 5)) };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _pollTimer.Start();
    }

    private async Task PollAsync()
    {
        // Guard anti-reentrancia: con timeout 30s e intervalo 5s pueden
        // solaparse varios PollAsync sobre el mismo device_code si la red va
        // lenta. El flag asegura que solo uno este en vuelo; los ticks
        // subsiguientes se ignoran y el DispatcherTimer reintenta solo.
        if (_isPolling) return;
        _isPolling = true;
        try
        {
            var remaining = (int)(_expiresAt - DateTime.UtcNow).TotalSeconds;
            if (remaining <= 0)
            {
                _pollTimer?.Stop();
                StatusText.Text = "Codigo expirado. Cierra y vuelve a intentar.";
                StatusText.Foreground = Brushes.Red;
                Progress.IsIndeterminate = false;
                return;
            }
            TimeText.Text = $"Tiempo restante: {remaining / 60} min {remaining % 60} seg";

            try
            {
                var token = await _gh.PollAccessTokenAsync(_deviceCode);
                if (!string.IsNullOrEmpty(token))
                {
                    _pollTimer?.Stop();
                    StatusText.Text = "Autorizado! Sesion iniciada.";
                    StatusText.Foreground = Brushes.Green;
                    Progress.IsIndeterminate = false;
                    Progress.Value = 100;
                    await Task.Delay(600);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    // pending / slow_down: si un poll previo mostro un error
                    // transitorio (timeout / sin conexion), restauramos al mensaje
                    // normal para que el alumno vea que la app responde.
                    StatusText.Text = "Esperando que ingreses el codigo...";
                    StatusText.Foreground = Brushes.Black;
                }
            }
            catch (TimeoutException ex)
            {
                // Codigo de GitHub expirado: fatal. Cortamos y avisamos.
                _pollTimer?.Stop();
                StatusText.Text = $"Codigo expirado. {ex.Message}";
                StatusText.Foreground = Brushes.Red;
                Progress.IsIndeterminate = false;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Acceso denegado por el alumno: fatal.
                _pollTimer?.Stop();
                StatusText.Text = $"Acceso denegado: {ex.Message}";
                StatusText.Foreground = Brushes.Red;
                Progress.IsIndeterminate = false;
            }
            catch (GitHubService.SlowDownException ex)
            {
                // GitHub pidio ir mas lento (rfc 8628). Aumentamos el intervalo del
                // timer en AddSeconds y seguimos reintentando, sin cortar.
                if (_pollTimer != null && _pollTimer.Interval < TimeSpan.FromMinutes(1))
                    _pollTimer.Interval += TimeSpan.FromSeconds(ex.AddSeconds);
                StatusText.Text = $"Reintentando... ({ex.Message})";
                StatusText.Foreground = Brushes.Orange;
            }
            catch (TaskCanceledException ex)
            {
                // Timeout de red del HttpClient (30s sin respuesta). Transitorio:
                // redes de aula lentas o saturadas pueden recuperarse. Mostramos
                // el motivo Y seguimos reintentando (NO cortamos el timer).
                StatusText.Text = $"Reintentando... (timeout: {ex.Message})";
                StatusText.Foreground = Brushes.Orange;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                // Sin ruta a GitHub: DNS / proxy del aula bloqueando / firewall de
                // Windows negando la app / AV haciendo MITM del TLS. Transitorio:
                // mostramos el motivo Y seguimos reintentando.
                StatusText.Text = $"Reintentando... (sin conexion: {ex.Message})";
                StatusText.Foreground = Brushes.Orange;
            }
            catch (InvalidOperationException ex)
            {
                // JSON inesperado o error desconocido de GitHub. Transitorio.
                StatusText.Text = $"Reintentando... ({ex.Message})";
                StatusText.Foreground = Brushes.Orange;
            }
            catch (Exception ex)
            {
                // Ultimo recurso: cualquier otra excepcion. La mostramos tambien
                // para no volver nunca al patron catch {} silencioso que oculto el
                // bug original del "queda esperando".
                StatusText.Text = $"Reintentando... ({ex.GetType().Name}: {ex.Message})";
                StatusText.Foreground = Brushes.Orange;
            }
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_verifyUri); CopyUrlButton.Content = "Copiado!"; } catch { }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(CodeText.Text); CopyCodeButton.Content = "Copiado!"; } catch { }
    }

    // Unica opcion: abrir el device flow en el navegador embebido (WebView2)
    // endurecido. El polling del token NO cambia: sigue corriendo en PollAsync.
    // El alumno solo pega el codigo dentro de esta ventana. NO hay fallback al
    // navegador externo: github.com/login/device esta en la whitelist y, si
    // WebView2 falla, la propia ventana avisa y se cierra.
    private void OpenEmbedded_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_verifyUri)) return;
        var ctx = new BrowseContext
        {
            GithubUsername = "(login)",
            PcName = Environment.MachineName,
            Section = StudentSection.Get()
        };
        var win = new WebBrowserWindow(_verifyUri, "Iniciar sesion en GitHub", ctx, OnForbiddenNavigation) { Owner = this };
        win.Show();
    }

    // Trampa por navegacion fuera de la whitelist durante el login. El device
    // flow solo deberia tocar dominios permitidos (github / microsoft / google),
    // asi que salir de ahi se trata como intento de evasion.
    private void OnForbiddenNavigation(string host)
    {
        LockdownService.Trigger($"Navegacion prohibida: {host}", 0, new[] { $"Navegacion prohibida: {host}" });
        var alert = new CheatWindow($"Navegacion prohibida: {host}", 0, new[] { $"Navegacion prohibida: {host}" }) { Owner = this };
        alert.ShowDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _pollTimer?.Stop();
        DialogResult = false;
        Close();
    }
}
