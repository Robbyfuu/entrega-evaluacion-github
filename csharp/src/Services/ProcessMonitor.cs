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

    /// <summary>
    /// Wrapper retrocompatible: evalua contra la lista hardcodeada (fallback).
    /// Equivale a llamar el overload con dynamicSet=null.
    /// </summary>
    public static bool IsSuspicious(string processName)
        => IsSuspicious(processName, null);

    /// <summary>
    /// True si el proceso es sospechoso. Normaliza el nombre (paridad C#/SQL/TS)
    /// y consulta el set dinamico cacheado de la tabla `suspicious_processes`.
    ///
    /// Fallback explicito: si <paramref name="dynamicSet"/> es null O vacio se usa
    /// <see cref="Config.SuspiciousProcesses"/>. El caller senala "fetch fallido o
    /// tabla vacia" pasando null (GetBlocklistAsync devuelve null en ambos casos),
    /// asi la cobertura de deteccion NUNCA cae a cero.
    /// </summary>
    public static bool IsSuspicious(string processName, IReadOnlySet<string>? dynamicSet)
    {
        var normalized = Config.NormalizeProcessName(processName);
        if (dynamicSet is { Count: > 0 })
            return dynamicSet.Contains(normalized);
        return Config.SuspiciousProcesses.Contains(normalized);
    }
}
