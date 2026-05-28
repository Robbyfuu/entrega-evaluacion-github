using System.Windows.Forms;
using EntregaEvaluacion.Forms;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        // Single-instance: si ya corre (demonio re-lanzo), salir.
        _mutex = new Mutex(initiallyOwned: true, "EntregaEvaluacion_SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Ya hay una instancia. Esta sobra (demonio la lanzo de mas).
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Registrar demonio (scheduled task) para auto-restart
        try { DaemonService.EnsureRegistered(); } catch { }

        // Auto-reconciliar bloqueo de internet al iniciar (fail-safe)
        try { InternetBlockService.ReconcileOnStartup(); } catch { }

        // Si hay lockdown persistente de sesion anterior, mostrarlo primero
        try
        {
            if (LockdownService.HasPersistentMarker())
            {
                using var alert = new CheatAlertForm(
                    repoName: "(sesion anterior)",
                    filesCount: 0,
                    filesSample: new[] { "Bloqueo detectado en sesion anterior" },
                    isPersistent: true,
                    remoteSource: false);
                alert.ShowDialog();
            }
        }
        catch { }

        Application.Run(new MainForm());
    }
}
