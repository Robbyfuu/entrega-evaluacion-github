using System.Windows;
using Microsoft.Web.WebView2.Core;
using EntregaEvaluacion.Models;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Navegador embebido (WebView2 / Edge-Chromium) endurecido. Abre Classroom,
/// signup, login de GitHub/Entra/Google y device flow DENTRO de la app.
///
/// Seguridad:
///  - Whitelist de dominios (Config.AllowedBrowseDomains). Toda navegacion se
///    intercepta ANTES de cargar (NavigationStarting). Dominio permitido =>
///    se carga y se registra (allowed:true). Dominio prohibido => se cancela,
///    se registra (allowed:false) y se DISPARA LA TRAMPA via el callback del
///    owner (lockdown + pantalla roja).
///  - NewWindowRequested: los popups se redirigen a la misma webview (pasan
///    por el mismo filtro), nunca abren ventana nueva ni navegador externo.
///  - Sin fallback al navegador externo. Si WebView2 no esta disponible, se
///    avisa y se cierra: NO se permite escapar del sandbox.
/// </summary>
public partial class WebBrowserWindow : Window
{
    private readonly string _url;
    private readonly BrowseContext _ctx;
    private readonly Action<string> _onForbiddenNavigation;
    private readonly SupabaseClient _sb = new();

    // Host de la URL inicial: siempre se permite (es el destino legitimo con el
    // que se abrio la ventana, aunque su dominio no este en la whitelist).
    private readonly string _initialHost;
    private bool _initialAllowed;

    // Una vez disparada la trampa, no seguimos procesando navegaciones.
    private bool _forbiddenTriggered;

    // Allowlist dinamica (tabla allowed_urls, editable desde el panel). Se
    // fetchea una vez en InitAsync ANTES de la primera navegacion. null/vacio
    // => Config.IsUrlAllowed cae al hardcode (fallback seguro).
    private IReadOnlyList<AllowedUrl>? _allowlist;

    public WebBrowserWindow(string url, string title, BrowseContext ctx, Action<string> onForbiddenNavigation)
    {
        InitializeComponent();
        _url = url;
        _ctx = ctx;
        _onForbiddenNavigation = onForbiddenNavigation;
        _initialHost = TryGetHost(url);
        Title = title;
        UrlText.Text = url;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            // WebView2 respeta el proxy del sistema por defecto. Durante el bloqueo
            // de internet, InternetBlockService pone un proxy blackhole
            // (127.0.0.1:1) en HKCU; eso dejaria al navegador embebido sin red y el
            // device flow de GitHub nunca podria autorizar (el polling gira eterno).
            // Igual que los HttpClient usan UseProxy=false, forzamos --no-proxy-server:
            // la whitelist de dominios (NavigationStarting) sigue siendo el control de
            // seguridad real del sandbox, no el proxy del sistema.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EntregaEvaluacion", "WebView2");
            var options = new CoreWebView2EnvironmentOptions("--no-proxy-server");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);

            // Modo InPrivate: el navegador embebido usa un perfil en memoria y NO
            // persiste cookies, cache ni la sesion de GitHub del alumno. En un PC de
            // examen compartido, cada alumno se loguea desde cero y no queda rastro
            // del anterior. El token queda solo en token.dat (DPAPI), nunca en WebView2.
            var controllerOptions = env.CreateCoreWebView2ControllerOptions();
            controllerOptions.IsInPrivateModeEnabled = true;
            await Browser.EnsureCoreWebView2Async(env, controllerOptions);
        }
        catch (Exception)
        {
            // Runtime de WebView2 ausente o fallo de inicializacion. NO abrimos
            // navegador externo (seria un escape del sandbox). Avisamos y cerramos.
            MessageBox.Show(
                "Falta el componente WebView2 en este equipo.\n\n" +
                "Avisa al profesor. No es posible continuar por seguridad.",
                "Componente requerido no disponible",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        var core = Browser.CoreWebView2;

        // Interceptar ANTES de cargar cada navegacion (whitelist + tracking + trampa).
        core.NavigationStarting += CoreWebView2_NavigationStarting;

        // Popups / target=_blank: redirigir a la misma webview (mismo filtro),
        // nunca abrir ventana nueva ni navegador externo.
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

        // Fetch de la allowlist dinamica ANTES de la primera navegacion (la
        // URL inicial siempre se permite por _initialHost, pero los redirects
        // SSO posteriores ya encuentran la lista lista). Si falla => null =>
        // Config.IsUrlAllowed cae al hardcode.
        _allowlist = await _sb.GetAllowlistAsync(_ctx.Section);

        Browser.Source = new Uri(_url);
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_forbiddenTriggered) { e.Cancel = true; return; }

        var uri = e.Uri ?? "";

        // about:blank / data: y esquemas no http(s) -> permitir sin tracking.
        if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            return;

        var host = TryGetHost(uri);

        // La primera navegacion a la URL inicial pasada al constructor se permite
        // siempre (destino legitimo de la ventana). Solo la primera vez.
        if (!_initialAllowed && !string.IsNullOrEmpty(host) &&
            host.Equals(_initialHost, StringComparison.OrdinalIgnoreCase))
        {
            _initialAllowed = true;
            _ = _sb.ReportBrowsingAsync(
                _ctx.GithubUsername, _ctx.PcName, _ctx.Section, uri, host, allowed: true);
            return;
        }

        if (Config.IsUrlAllowed(uri, _allowlist))
        {
            // Permitida (host en whitelist o URL exacta permitida): registrar.
            _ = _sb.ReportBrowsingAsync(
                _ctx.GithubUsername, _ctx.PcName, _ctx.Section, uri, host, allowed: true);
            return;
        }

        // Dominio prohibido: bloquear, registrar y disparar la trampa.
        e.Cancel = true;
        _forbiddenTriggered = true;
        _ = _sb.ReportBrowsingAsync(
            _ctx.GithubUsername, _ctx.PcName, _ctx.Section, uri, host, allowed: false);

        var forbiddenHost = string.IsNullOrEmpty(host) ? uri : host;

        // Cerrar esta ventana y delegar al owner el flujo de lockdown (tiene el
        // contexto: _sb, _gh, user, section). Hacemos el cierre + callback en el
        // dispatcher para no chocar con el handler de navegacion en curso.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { Close(); } catch { }
            _onForbiddenNavigation(forbiddenHost);
        }));
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // No abrir ventana nueva: navegar en la misma webview. El destino pasa
        // por NavigationStarting => mismo filtro de whitelist + trampa.
        e.Handled = true;
        if (_forbiddenTriggered) return;
        if (!string.IsNullOrEmpty(e.Uri))
        {
            try { Browser.CoreWebView2.Navigate(e.Uri); } catch { }
        }
    }

    private static string TryGetHost(string url)
    {
        try { return new Uri(url).Host ?? ""; }
        catch { return ""; }
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
