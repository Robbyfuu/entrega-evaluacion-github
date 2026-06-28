using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Tests del set-diff PURO de procesos sospechosos
/// (NewSuspiciousProcesses.Diff), la unica logica nueva del extraction
/// HeartbeatReporter. Congelan el comportamiento del SendHeartbeatAsync original:
/// se alerta UNA sola vez por proceso nuevo+sospechoso, las claves vistas se
/// reconstruyen completas y el matching de claves es ordinal (case-sensitive).
/// El predicado de sospecha se inyecta (en produccion es
/// ProcessMonitor.IsSuspicious(name, blocklist)); aca se usan predicados simples.
/// </summary>
public class NewSuspiciousProcessesTests
{
    private static IReadOnlySet<string> Seen(params string[] keys) => new HashSet<string>(keys);

    private static ObservedProcess P(string name, int pid, string title = "t") => new(name, pid, title);

    // Predicado de sospecha por nombre exacto (case-sensitive) para aislar el
    // set-diff de la normalizacion real.
    private static System.Func<string, bool> SuspiciousNames(params string[] names)
    {
        var set = new HashSet<string>(names);
        return set.Contains;
    }

    [Fact]
    public void Key_FormatsNameColonPid()
    {
        Assert.Equal("chrome:42", NewSuspiciousProcesses.Key("chrome", 42));
    }

    [Fact]
    public void NewSuspicious_Appears_IsReported()
    {
        var result = NewSuspiciousProcesses.Diff(
            new[] { P("chrome", 1) },
            Seen(),
            SuspiciousNames("chrome"));

        Assert.Single(result.NewlySuspicious);
        Assert.Equal(P("chrome", 1), result.NewlySuspicious[0]);
        Assert.Contains("chrome:1", result.SeenKeys);
    }

    [Fact]
    public void AlreadySeen_Suspicious_IsSuppressed()
    {
        var result = NewSuspiciousProcesses.Diff(
            new[] { P("chrome", 1) },
            Seen("chrome:1"),
            SuspiciousNames("chrome"));

        Assert.Empty(result.NewlySuspicious);
        // Sigue presente en el set reconstruido (se sigue rastreando).
        Assert.Contains("chrome:1", result.SeenKeys);
    }

    [Fact]
    public void NewButNotSuspicious_NotReported_ButTracked()
    {
        var result = NewSuspiciousProcesses.Diff(
            new[] { P("notepad", 1) },
            Seen(),
            SuspiciousNames("chrome"));

        Assert.Empty(result.NewlySuspicious);
        // Aunque no sea sospechoso, su clave entra al set visto (igual que el
        // original: `current` acumula TODAS las claves del tick).
        Assert.Contains("notepad:1", result.SeenKeys);
    }

    [Fact]
    public void Disappeared_ThenReappeared_ReportsAgain()
    {
        // Tick 1: chrome:1 visto.
        var tick1 = NewSuspiciousProcesses.Diff(
            new[] { P("chrome", 1) }, Seen(), SuspiciousNames("chrome"));
        Assert.Single(tick1.NewlySuspicious);

        // Tick 2: chrome desaparece -> el set visto queda vacio.
        var tick2 = NewSuspiciousProcesses.Diff(
            System.Array.Empty<ObservedProcess>(), tick1.SeenKeys, SuspiciousNames("chrome"));
        Assert.Empty(tick2.NewlySuspicious);
        Assert.Empty(tick2.SeenKeys);

        // Tick 3: chrome reaparece -> como no estaba en el prior set, se reporta
        // de nuevo.
        var tick3 = NewSuspiciousProcesses.Diff(
            new[] { P("chrome", 1) }, tick2.SeenKeys, SuspiciousNames("chrome"));
        Assert.Single(tick3.NewlySuspicious);
        Assert.Equal(P("chrome", 1), tick3.NewlySuspicious[0]);
    }

    [Fact]
    public void Empty_Current_ProducesNothing()
    {
        var result = NewSuspiciousProcesses.Diff(
            System.Array.Empty<ObservedProcess>(),
            Seen("chrome:1"),
            SuspiciousNames("chrome"));

        Assert.Empty(result.NewlySuspicious);
        // Sin procesos actuales, el set visto se vacia (los desaparecidos se
        // olvidan), igual que `current` en el original.
        Assert.Empty(result.SeenKeys);
    }

    [Fact]
    public void KeyMatching_IsCaseSensitive_Ordinal()
    {
        // "Chrome:1" (mayuscula) NO matchea el prior "chrome:1" (minuscula):
        // la clave es ordinal, asi que se considera un proceso NUEVO.
        var result = NewSuspiciousProcesses.Diff(
            new[] { P("Chrome", 1) },
            Seen("chrome:1"),
            SuspiciousNames("Chrome"));

        Assert.Single(result.NewlySuspicious);
        Assert.Equal(P("Chrome", 1), result.NewlySuspicious[0]);
        Assert.Contains("Chrome:1", result.SeenKeys);
    }

    [Fact]
    public void SameName_DifferentPid_IsNewProcess()
    {
        // chrome:1 ya visto; chrome:2 es una instancia nueva -> se reporta.
        var result = NewSuspiciousProcesses.Diff(
            new[] { P("chrome", 1), P("chrome", 2) },
            Seen("chrome:1"),
            SuspiciousNames("chrome"));

        Assert.Single(result.NewlySuspicious);
        Assert.Equal(2, result.NewlySuspicious[0].Pid);
        Assert.Contains("chrome:1", result.SeenKeys);
        Assert.Contains("chrome:2", result.SeenKeys);
    }

    [Fact]
    public void NewlySuspicious_PreservesInputOrder_AndFiltersNonSuspicious()
    {
        var result = NewSuspiciousProcesses.Diff(
            new[]
            {
                P("chrome", 1),   // nuevo + sospechoso  -> reportado
                P("notepad", 2),  // nuevo, no sospechoso -> filtrado
                P("discord", 3),  // nuevo + sospechoso  -> reportado
            },
            Seen(),
            SuspiciousNames("chrome", "discord"));

        Assert.Equal(2, result.NewlySuspicious.Count);
        Assert.Equal("chrome", result.NewlySuspicious[0].Name);
        Assert.Equal("discord", result.NewlySuspicious[1].Name);
        Assert.Equal(3, result.SeenKeys.Count);
    }
}
