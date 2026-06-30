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
/// persistente, antes de armar la ventana principal (que arranca OCULTA en la
/// bandeja del sistema).
///
/// App.xaml es ApplicationDefinition: WPF genera el Main (InitializeComponent +
/// Run). Toda la coordinacion ocurre en OnStartup, que corre antes de mostrar
/// cualquier ventana.
///
/// Activacion cross-proceso: ademas del mutex single-instance, un EventWaitHandle
/// con nombre permite que un segundo lanzamiento REAL del usuario le pida a la
/// instancia viva que se restaure desde la bandeja, sin abrir un proceso de mas.
/// El watchdog (relanzamiento cada 3 min via Scheduled Task con --watchdog) NUNCA
/// senaliza: jamas debe hacer aparecer la ventana.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "EntregaEvaluacion_SingleInstance";
    private const string ShowEventName = "EntregaEvaluacion_ShowRequest";

    private static Mutex? _mutex;

    // Evento de activacion (AutoReset): la instancia viva lo espera; un segundo
    // lanzamiento real lo Set para pedir restaurar la ventana desde la bandeja.
    private static EventWaitHandle? _showEvent;

    // Senal de parada limpia del waiter: se levanta y se Set el evento en OnExit.
    private static volatile bool _stopWaiter;

    // Referencia a la ventana principal para el waiter. Se asigna DESPUES de que
    // StartShell la construye; el waiter la chequea por null (la senal puede
    // llegar antes de que exista la ventana).
    private static MainWindow? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Velopack DEBE correr antes que nada. Maneja hooks de install/update/
        // uninstall y sale del proceso si corresponde.
        try { UpdateService.HandleStartup(e.Args); } catch { }

        // El watchdog (Scheduled Task cada 3 min) relanza el exe con --watchdog.
        // Se usa para NO senalizar activacion cuando esta instancia sobra.
        bool isWatchdog = e.Args.Contains("--watchdog");

        // Single-instance: si ya corre, esta instancia sobra.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isNew);
        if (!isNew)
        {
            // Ya hay una instancia viva.
            if (!isWatchdog)
            {
                // Lanzamiento REAL del usuario (doble clic en el acceso directo):
                // pedirle a la instancia viva que se restaure desde la bandeja.
                try
                {
                    var ev = EventWaitHandle.OpenExisting(ShowEventName);
                    ev.Set();
                }
                catch
                {
                    // La instancia viva puede no haber creado el evento todavia
                    // (arrancando). Es best-effort: igual salimos.
                }
            }
            // Watchdog: NO senaliza. El relanzamiento cada 3 min jamas debe hacer
            // aparecer la ventana. Solo sale.
            Current.Shutdown();
            return;
        }

        // Primera instancia: crear el evento de activacion y arrancar el waiter
        // ANTES del shell, asi un segundo lanzamiento durante el arranque ya lo
        // encuentra (el waiter ignora la senal mientras _shell sea null).
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        StartActivationWaiter();

        StartShell();
    }

    /// <summary>
    /// Hilo background que espera senales de activacion y restaura la ventana
    /// desde la bandeja en el hilo de UI. Para limpio en OnExit (_stopWaiter +
    /// Set del evento). No crashea si la senal llega antes de existir la ventana
    /// (chequeo de null) ni si el dispatcher esta cerrandose (try/catch).
    /// </summary>
    private static void StartActivationWaiter()
    {
        var t = new Thread(() =>
        {
            while (!_stopWaiter)
            {
                try
                {
                    var ev = _showEvent;
                    if (ev == null) break;
                    ev.WaitOne();
                    if (_stopWaiter) break;

                    var app = Current;
                    var shell = _shell;
                    if (app == null || shell == null) continue; // senal antes de la ventana
                    app.Dispatcher.Invoke(() =>
                    {
                        try { shell.RestoreFromTray(); } catch { }
                    });
                }
                catch
                {
                    // Dispatcher cerrandose u objeto liberado: salir si corresponde.
                    if (_stopWaiter) break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "ActivationWaiter",
        };
        t.Start();
    }

    /// <summary>
    /// Arranque real cuando esta es la unica instancia: demonio, internet,
    /// lockdown persistente y la ventana principal (que queda OCULTA en bandeja).
    /// </summary>
    private void StartShell()
    {
        // El shutdown se controla manualmente durante toda la vida del proceso:
        // ocultar la ventana en la bandeja NO debe matar el monitoreo. La unica
        // salida real es la ruta autorizada del ExitGuard (clave del profesor).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Aplicar el tema guardado (claro/oscuro) antes de mostrar ventanas.
        try { ThemeService.ApplySaved(); } catch { }

        // Registrar demonio (scheduled task) para auto-restart
        try { DaemonService.EnsureRegistered(); } catch { }

        // Reconciliar bloqueo de internet al iniciar (fail-safe)
        try { InternetBlockService.ReconcileOnStartup(); } catch { }

        // Si hay lockdown persistente de sesion anterior, mostrarlo primero. Este
        // chequeo corre ANTES de armar la ventana (y su icono de bandeja): el
        // bloqueo persistente debe ganar y bloquear antes de cualquier init de UI.
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
        _shell = main;

        // Arranque OCULTO en bandeja: Show()+Hide() conecta la ventana a un
        // PresentationSource para que dispare Loaded (y con el InitAsync: combos,
        // identidad y el timer admin que sostiene el monitoreo) SIN dejarla
        // visible ni en la barra de tareas (ShowInTaskbar/ShowActivated=False en
        // el XAML evitan el flash y el robo de foco). Se restaura desde la bandeja
        // (icono, doble clic, "Abrir") o por activacion cross-proceso.
        main.Show();
        main.Hide();
    }

    /// <summary>
    /// Cierre real del proceso: detener el waiter de activacion y liberar los
    /// objetos de sincronizacion con nombre (mutex + evento).
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _stopWaiter = true;
        try { _showEvent?.Set(); } catch { }
        try { _showEvent?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
