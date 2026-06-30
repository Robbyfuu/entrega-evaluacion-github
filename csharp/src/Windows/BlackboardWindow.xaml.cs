using System.Windows;
using System.Windows.Threading;
using EntregaEvaluacion.Services;
using Microsoft.Web.WebView2.Core;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Navegador embebido (WebView2 / Edge-Chromium) dedicado a la ENTREGA EN
/// BLACKBOARD (DUOC AVA), que corre DESPUES de subir la entrega a GitHub y
/// SOLO cuando la evaluacion lo exige (evaluations.requires_blackboard).
///
/// A diferencia de <see cref="WebBrowserWindow"/> (el navegador del examen):
///  - NO aplica la whitelist de dominios ni la trampa: esto corre
///    post-entrega, con internet disponible; el alumno debe poder loguearse
///    en DUOC, Entra/Microsoft y navegar Blackboard libremente.
///  - HABILITA el drag-drop de archivos del SO hacia la pagina
///    (AllowExternalDrop=true): la subida del zip a Blackboard depende de
///    poder arrastrar el archivo a la zona de carga.
///  - Reutiliza el MISMO userDataFolder y las MISMAS opciones de entorno
///    (--no-proxy-server) que el navegador del examen, pero con un perfil
///    PERSISTENTE (no InPrivate), para que una sesion DUOC ya iniciada se
///    mantenga.
///
/// IMPORTANTE: esta ventana NO toca ni debilita el lockdown del examen
/// (CheatWindow / bloqueos): es una ventana separada, post-entrega.
/// </summary>
public partial class BlackboardWindow : Window
{
    private readonly string _url;

    // ANTI-ESCAPE: esta ventana usa --no-proxy-server + sin whitelist, asi que es
    // un navegador SIN restricciones. El gate de apertura (RunBlackboardSubmission)
    // solo abre con internet liberado, pero si el examen RE-BLOQUEA internet
    // mientras la ventana esta abierta, Block() NO mata el proceso msedgewebview2
    // y --no-proxy-server ignora el proxy reaplicado => quedaria como navegador
    // libre persistente. Este timer la cierra sola apenas IsBlocked() vuelva a true.
    private readonly DispatcherTimer _internetGuard;

    public BlackboardWindow(string url, string? zipFileName = null)
    {
        InitializeComponent();
        _url = url;
        UrlText.Text = url;
        if (!string.IsNullOrWhiteSpace(zipFileName))
        {
            HintText.Text =
                $"Inicia sesion con tu cuenta DUOC, abre la tarea del examen final, pega el enlace " +
                $"(Ctrl+V) y ARRASTRA el archivo {zipFileName} (en la carpeta que se abrio) al area de subida.";
        }

        _internetGuard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _internetGuard.Tick += (_, _) =>
        {
            if (InternetBlockService.IsBlocked())
            {
                _internetGuard.Stop();
                try { Close(); } catch { }
            }
        };
        Loaded += (_, _) => _internetGuard.Start();
        Closed += (_, _) => _internetGuard.Stop();

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            // Mismo userDataFolder y mismas opciones de entorno que el navegador
            // del examen. --no-proxy-server es clave: durante el bloqueo de
            // internet el sistema tiene un proxy blackhole (127.0.0.1:1) en HKCU;
            // sin este flag, WebView2 quedaria sin red y Blackboard nunca cargaria.
            // Mantener las mismas opciones evita conflictos si el navegador del
            // examen sigue vivo (mismo userDataFolder => mismas opciones).
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EntregaEvaluacion", "WebView2");
            var options = new CoreWebView2EnvironmentOptions("--no-proxy-server");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);

            // Perfil PERSISTENTE (NO InPrivate): a diferencia del examen, aca
            // queremos que una sesion DUOC iniciada se mantenga durante la entrega.
            await Browser.EnsureCoreWebView2Async(env);
        }
        catch (Exception)
        {
            // Runtime de WebView2 ausente o fallo de init. La entrega a GitHub YA
            // se completo: no rompemos nada, solo avisamos y cerramos esta ventana.
            MessageBox.Show(
                "No se pudo abrir el navegador de Blackboard en este equipo.\n\n" +
                "Tu entrega a GitHub YA quedo subida y el enlace esta en el portapapeles.\n" +
                "Abre Blackboard manualmente para subir el zip y pegar el enlace, o avisa al profesor.",
                "Blackboard no disponible",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            try { Close(); } catch { }
            return;
        }

        // Drag-drop de archivos del SO hacia la pagina (subida del zip). Por
        // defecto el control WPF ya lo trae en true; lo dejamos explicito.
        Browser.AllowExternalDrop = true;

        var core = Browser.CoreWebView2;

        // Popups / target=_blank: navegar en la misma webview (sin abrir ventana
        // nueva ni navegador externo). No hay whitelist que aplicar aca.
        core.NewWindowRequested += CoreWebView2_NewWindowRequested;

        Browser.CoreWebView2.SourceChanged += (_, _) =>
        {
            UrlText.Text = Browser.Source?.ToString() ?? "";
            BackButton.IsEnabled = Browser.CanGoBack;
        };
        Browser.NavigationCompleted += (_, _) =>
        {
            BackButton.IsEnabled = Browser.CanGoBack;
        };

        Browser.Source = new Uri(_url);
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Mantener la navegacion dentro de la misma webview (logins SSO de DUOC
        // suelen abrir popups). No abrimos ventana nueva ni navegador externo.
        e.Handled = true;
        if (!string.IsNullOrEmpty(e.Uri))
        {
            try { Browser.CoreWebView2.Navigate(e.Uri); } catch { }
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        try { if (Browser.CanGoBack) Browser.GoBack(); } catch { }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        try { Browser.Reload(); } catch { }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
