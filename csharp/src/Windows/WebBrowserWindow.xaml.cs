using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Navegador embebido (WebView2 / Edge-Chromium) para abrir Classroom, signup y
/// el device flow de GitHub DENTRO de la app, sin depender del navegador externo
/// ni del estado de bloqueo de internet del sistema.
///
/// Si el runtime de WebView2 no esta presente o falla al inicializar, NO se
/// crashea: se hace fallback a abrir la URL en el navegador externo del sistema
/// y se avisa al alumno.
/// </summary>
public partial class WebBrowserWindow : Window
{
    private readonly string _url;
    private bool _initFailed;

    public WebBrowserWindow(string url, string title)
    {
        InitializeComponent();
        _url = url;
        Title = title;
        UrlText.Text = url;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();

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
        catch (Exception ex)
        {
            // Runtime ausente o fallo de inicializacion: fallback externo.
            _initFailed = true;
            FallbackToExternal(ex);
        }
    }

    private void FallbackToExternal(Exception? ex = null)
    {
        try { Process.Start(new ProcessStartInfo { FileName = _url, UseShellExecute = true }); }
        catch { }

        MessageBox.Show(
            "No se pudo abrir el navegador integrado en esta PC.\n\n" +
            "Se abrio la pagina en tu navegador externo:\n" + _url,
            "Navegador integrado no disponible",
            MessageBoxButton.OK, MessageBoxImage.Information);

        Close();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_initFailed) return;
        try { if (Browser.CanGoBack) Browser.GoBack(); } catch { }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_initFailed) return;
        try { Browser.Reload(); } catch { }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
