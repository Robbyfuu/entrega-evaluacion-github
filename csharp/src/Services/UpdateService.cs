using Velopack;
using Velopack.Sources;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Auto-update via Velopack desde GitHub Releases. Chequea, descarga y aplica
/// updates en background. Coordina con el demonio para no pelear en el reinicio.
/// </summary>
public static class UpdateService
{
    // Repo de releases. Velopack busca assets generados por 'vpk pack'.
    private const string RepoUrl = "https://github.com/Robbyfuu/entrega-evaluacion-github";

    private static UpdateManager? _mgr;

    /// <summary>
    /// Llamar lo antes posible en Main(). Maneja los hooks de instalacion de
    /// Velopack (primer install, update, uninstall) y SALE si corresponde.
    /// </summary>
    public static void HandleStartup(string[] args)
    {
        VelopackApp.Build()
            .WithFirstRun(_ => { /* primer arranque tras instalar */ })
            .Run();
    }

    /// <summary>
    /// Chequea + descarga + aplica update en background. Si hay update, lo deja
    /// listo y reinicia la app. Silencioso si no hay update o no hay internet.
    /// Devuelve true si va a reiniciar (el caller debe dejar de trabajar).
    /// </summary>
    public static async Task<bool> CheckAndApplyAsync(Action<string>? log = null)
    {
        try
        {
            _mgr = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));

            // No instalado via Velopack (ej. corriendo en dev) -> no update
            if (!_mgr.IsInstalled)
            {
                log?.Invoke("[update] no instalado via Velopack, skip.");
                return false;
            }

            var info = await _mgr.CheckForUpdatesAsync();
            if (info == null)
            {
                log?.Invoke("[update] ya estas en la ultima version.");
                return false;
            }

            log?.Invoke($"[update] nueva version {info.TargetFullRelease.Version}. Descargando...");
            await _mgr.DownloadUpdatesAsync(info);

            log?.Invoke("[update] descargada. Reiniciando para aplicar...");

            // Pausar demonio para que no relance una instancia vieja durante el swap
            try { DaemonService.Unregister(); } catch { }

            _mgr.ApplyUpdatesAndRestart(info);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[update] error (ignorado): {ex.Message}");
            return false;
        }
    }

    /// <summary>Version actual de la app (para reportar al panel).</summary>
    public static string CurrentVersion()
    {
        try
        {
            return _mgr?.CurrentVersion?.ToString()
                ?? System.Reflection.Assembly.GetEntryAssembly()?
                    .GetName().Version?.ToString() ?? "dev";
        }
        catch { return "dev"; }
    }
}
