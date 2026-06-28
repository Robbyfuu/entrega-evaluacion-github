using System.Threading;
using System.Windows;
using EntregaEvaluacion.Core;
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
                // remoteSource + checkStillLocked por OVERRIDE DE PC: en el lab el
                // PC pudo quedar bloqueado de una sesion de otro alumno, sin sesion
                // activa aca. El profe lo libera por NOMBRE de PC (pc_overrides),
                // sin depender del usuario. Fail-safe: solo libera con un
                // unblock_screen=true confirmado; error de red => sigue bloqueado.
                var sb = new SupabaseClient();
                var pc = Environment.MachineName;
                var alert = new CheatWindow(
                    repoName: "(sesion anterior)",
                    filesCount: 0,
                    filesSample: new[] { "Bloqueo detectado en sesion anterior" },
                    isPersistent: true,
                    remoteSource: true,
                    checkStillLocked: () => !System.Threading.Tasks.Task.Run(
                        () => sb.IsPcScreenUnblockedAsync(pc).GetAwaiter().GetResult()).GetAwaiter().GetResult());
                alert.ShowDialog();
            }
        }
        catch { }

        // Composition root: se arma el grafo de dependencias de la ventana
        // principal y se inyecta por constructor (ENT-6 step 5). Este SupabaseClient
        // es una instancia distinta de la usada arriba para el CheatWindow de
        // lockdown persistente; colapsarlas es ENT-7.
        var gh = new GitHubService();
        var selection = new SelectionStore(new RegistrySelectionPersistence());
        var mainSb = new SupabaseClient();
        var main = new MainWindow(gh, mainSb, selection);
        MainWindow = main;
        // A partir de aca, cerrar la ventana principal cierra la app.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }
}
