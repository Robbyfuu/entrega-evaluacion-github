namespace EntregaEvaluacion.Core;

/// <summary>
/// Proceso observado en un tick (proyeccion pura de Models.ProcessInfo, sin WPF
/// ni I/O). El caller (HeartbeatReporter) mapea su DTO a este record antes de
/// llamar al set-diff, asi la deteccion de "nuevos sospechosos" queda testeable
/// en EntregaEvaluacion.Core.
/// </summary>
public readonly record struct ObservedProcess(string Name, int Pid, string Title);

/// <summary>
/// Resultado del set-diff de un tick: los procesos que aparecieron NUEVOS y son
/// sospechosos (a reportar, en orden de entrada) + el conjunto COMPLETO de claves
/// vistas este tick (el nuevo "prior set" para el proximo tick).
/// </summary>
public readonly record struct NewSuspiciousResult(
    IReadOnlyList<ObservedProcess> NewlySuspicious,
    IReadOnlySet<string> SeenKeys);

/// <summary>
/// Logica PURA del set-diff de procesos sospechosos, extraida de
/// MainWindow.SendHeartbeatAsync. Es la unica logica nueva del extraction:
/// dado el conjunto de procesos del tick actual, las claves vistas en el tick
/// previo y un predicado de sospecha, determina cuales procesos son NUEVOS y
/// sospechosos (para alertar UNA sola vez) y reconstruye el conjunto de claves
/// vistas.
///
/// Preserva EXACTO el comportamiento original:
///   - clave = "{Name}:{Pid}" (ordinal, case-sensitive, igual que el
///     HashSet&lt;string&gt; por defecto de MainWindow).
///   - un proceso se reporta solo si su clave NO estaba en el prior set Y el
///     predicado de sospecha da true (short-circuit: el predicado no se evalua
///     para claves ya vistas).
///   - el nuevo "seen" contiene TODAS las claves del tick (no solo las
///     sospechosas), igual que el `current` original.
///   - el predicado se aplica sobre el Name CRUDO; la normalizacion vive en el
///     predicado que inyecta el caller (ProcessMonitor.IsSuspicious).
/// </summary>
public static class NewSuspiciousProcesses
{
    /// <summary>Clave de identidad de un proceso para el set-diff (ordinal).</summary>
    public static string Key(string name, int pid) => $"{name}:{pid}";

    public static NewSuspiciousResult Diff(
        IEnumerable<ObservedProcess> current,
        IReadOnlySet<string> previouslySeen,
        Func<string, bool> isSuspicious)
    {
        var seen = new HashSet<string>();
        var newlySuspicious = new List<ObservedProcess>();

        foreach (var p in current)
        {
            var key = Key(p.Name, p.Pid);
            seen.Add(key);
            // Nota: el chequeo es contra el prior set (previouslySeen), nunca
            // contra `seen`; agregar a `seen` arriba no afecta esta condicion.
            // Esto replica el original, donde `current.Add(key)` precede al
            // `if (!_lastProcSet.Contains(key) && ...)`.
            if (!previouslySeen.Contains(key) && isSuspicious(p.Name))
                newlySuspicious.Add(p);
        }

        return new NewSuspiciousResult(newlySuspicious, seen);
    }
}
