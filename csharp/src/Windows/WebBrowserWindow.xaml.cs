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
            await Browser.EnsureCoreWebView2Async();
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

        if (Config.IsDomainAllowed(host))
        {
            // Permitida: registrar (fire-and-forget).
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
