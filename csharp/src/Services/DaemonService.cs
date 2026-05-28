using System.Diagnostics;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Hace que la app corra como "demonio": registra una Scheduled Task de usuario
/// (sin admin) que la re-lanza al login y cada N minutos si no esta corriendo.
/// Asi sigue monitoreando aunque el alumno la cierre.
/// </summary>
public static class DaemonService
{
    private const string TaskName = "EntregaEvaluacionDaemon";

    public static void EnsureRegistered()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;

            // Si la task ya existe, no recrear
            if (TaskExists()) return;

            // Crear task user-level: AtLogon + repeticion cada 3 min por 1 dia.
            // /sc minute /mo 3 con /du no es directo en schtasks; usamos onlogon
            // + un trigger adicional via XML seria complejo. Estrategia simple:
            // onlogon para arrancar al inicio, y un watchdog interno (timer) que
            // re-registra si lo borran. Para repeticion robusta usamos schtasks
            // con /sc minute /mo 3.
            RunSchtasks($"/create /tn \"{TaskName}\" /tr \"\\\"{exe}\\\"\" /sc minute /mo 3 /f /rl limited");
        }
        catch { }
    }

    public static void Unregister()
    {
        try { RunSchtasks($"/delete /tn \"{TaskName}\" /f"); } catch { }
    }

    private static bool TaskExists()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/query /tn \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }
}
