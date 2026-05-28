using System.Diagnostics;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Enumera procesos con ventana visible. Excluye el propio proceso para no
/// auto-reportarse como sospechoso.
/// </summary>
public static class ProcessMonitor
{
    public static List<ProcessInfo> GetOpenWindows()
    {
        var result = new List<ProcessInfo>();
        var myPid = Environment.ProcessId;
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == myPid) continue;
                    if (string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    result.Add(new ProcessInfo
                    {
                        Name = p.ProcessName,
                        Title = p.MainWindowTitle,
                        Pid = p.Id
                    });
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    public static bool IsSuspicious(string processName)
    {
        return Config.SuspiciousProcesses.Contains(
            processName.ToLowerInvariant());
    }
}
