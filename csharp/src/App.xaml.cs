using System.Threading;
using System.Windows;
using EntregaEvaluacion.Services;
using EntregaEvaluacion.Windows;

namespace EntregaEvaluacion;

/// <summary>
/// Entry point WPF. Replica el orden de arranque del antiguo Program.cs dentro
/// de OnStartup: Velopack HandleStartup primero, mutex single-instance, registro
/// del demonio, reconciliacion del bloqueo de internet y chequeo de lockdown
/// persistente, antes de abrir la ventana principal.
///
/// App.xaml es ApplicationDefinition: WPF genera el Main (InitializeComponent +
/// Run). Toda la coordinacion ocurre en OnStartup, que corre antes de mostrar
/// cualquier ventana.
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Velopack DEBE correr antes que nada. Maneja hooks de install/update/
        // uninstall y sale del proceso si corresponde.
        try { UpdateService.HandleStartup(e.Args); } catch { }

        // Single-instance: si ya corre (demonio re-lanzo), salir sin abrir UI.
        _mutex = new Mutex(initiallyOwned: true, "EntregaEvaluacion_SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Ya hay una instancia. Esta sobra (demonio la lanzo de mas).
            Current.Shutdown();
        }
        else
        {
            StartShell();
        }
    }

    /// <summary>
    /// Arranque real cuando esta es la unica instancia: demonio, internet,
    /// lockdown persistente y la ventana principal.
    /// </summary>
    private void StartShell()
    {
        // Controlamos el shutdown manualmente mientras mostramos el CheatWindow
        // persistente como modal antes de abrir el MainWindow.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Aplicar el tema guardado (claro/oscuro) antes de mostrar ventanas.
        try { ThemeService.ApplySaved(); } catch { }

        // Registrar demonio (scheduled task) para auto-restart
        try { DaemonService.EnsureRegistered(); } catch { }

        // Reconciliar bloqueo de internet al iniciar (fail-safe)
        try { InternetBlockService.ReconcileOnStartup(); } catch { }

        // Si hay lockdown persistente de sesion anterior, mostrarlo primero
        try
        {
            if (LockdownService.HasPersistentMarker())
            {
                var alert = new CheatWindow(
                    repoName: "(sesion anterior)",
                    filesCount: 0,
                    filesSample: new[] { "Bloqueo detectado en sesion anterior" },
                    isPersistent: true,
                    remoteSource: false);
                alert.ShowDialog();
            }
        }
        catch { }

        var main = new MainWindow();
        MainWindow = main;
        // A partir de aca, cerrar la ventana principal cierra la app.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }
}
