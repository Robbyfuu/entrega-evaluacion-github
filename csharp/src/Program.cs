using System.Windows.Forms;
using EntregaEvaluacion.Forms;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

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
