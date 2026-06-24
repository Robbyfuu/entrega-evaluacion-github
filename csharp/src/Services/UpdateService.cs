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

    // True mientras se aplica un update (Velopack reinicia la app). MainWindow.
    // OnClosing lo consulta para NO bloquear el cierre durante la actualizacion.
    public static bool IsApplying { get; private set; }

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
    ///
    /// accessToken: token del alumno logueado. CLAVE en sala de examen: sin
    /// token, Velopack pega a la API de GitHub SIN autenticar (limite 60/hora
    /// POR IP). Con todos los PCs detras del mismo NAT, esos 60 se agotan y da
    /// 403 (rate limit) => nadie actualiza. Autenticado, el limite es 5000/hora
    /// POR USUARIO, asi cada alumno tiene su propio cupo y no se pisan.
    /// </summary>
    public static async Task<bool> CheckAndApplyAsync(Action<string>? log = null, string? accessToken = null)
    {
        try
        {
            var token = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
            _mgr = new UpdateManager(new GithubSource(RepoUrl, token, prerelease: false));

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

            IsApplying = true; // permite que OnClosing no bloquee el reinicio del update
            _mgr.ApplyUpdatesAndRestart(info);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[update] error (ignorado): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Version actual de la app (para mostrar en la UI y reportar al panel).
    /// Lazy-init del UpdateManager: leer CurrentVersion NO toca la red (solo
    /// CheckForUpdatesAsync lo hace), asi que es seguro instanciarlo aca para
    /// obtener la version REAL del paquete Velopack (la del tag de release),
    /// que puede diferir de la version del ensamblado. Fallback a assembly.
    /// </summary>
    public static string CurrentVersion()
    {
        try
        {
            _mgr ??= new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
            var v = _mgr.IsInstalled ? _mgr.CurrentVersion?.ToString() : null;
            return v
                ?? System.Reflection.Assembly.GetEntryAssembly()?
                    .GetName().Version?.ToString() ?? "dev";
        }
        catch
        {
            return System.Reflection.Assembly.GetEntryAssembly()?
                .GetName().Version?.ToString() ?? "dev";
        }
    }
}
