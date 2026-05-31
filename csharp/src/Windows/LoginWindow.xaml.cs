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
    private readonly GitHubService _gh;
    private DispatcherTimer? _pollTimer;
    private string _deviceCode = "";
    private DateTime _expiresAt;
    private string _verifyUri = "";

    public LoginWindow(GitHubService gh)
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
        }
        catch (TimeoutException)
        {
            _pollTimer?.Stop();
            StatusText.Text = "Codigo expirado.";
            StatusText.Foreground = Brushes.Red;
        }
        catch (UnauthorizedAccessException)
        {
            _pollTimer?.Stop();
            StatusText.Text = "Acceso denegado.";
            StatusText.Foreground = Brushes.Red;
        }
        catch { /* reintentar */ }
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
