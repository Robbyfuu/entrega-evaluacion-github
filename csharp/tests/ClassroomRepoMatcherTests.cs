using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests: congelan el comportamiento ACTUAL del matcher
/// LONGEST-PREFIX-WINS extraido de MainWindow.PickAssignmentByLongestPrefix.
/// El tipo de prueba es un record simple con Title/Org; un Id distingue
/// identidad para verificar determinismo ante empate total.
/// </summary>
public class ClassroomRepoMatcherTests
{
    private sealed record Candidate(string Id, string Title, string? Org);

    private static Candidate? Pick(
        IEnumerable<Candidate> candidates, string repoName, string? inviter, string? evalOrg)
        => ClassroomRepoMatcher.PickByLongestPrefix(
            candidates, repoName, inviter, evalOrg, c => c.Title, c => c.Org);

    [Fact]
    public void LongestPrefixWins_OverShorterPrefix()
    {
        var candidates = new[]
        {
            new Candidate("short", "Tarea", null),        // prefijo "tarea-"
            new Candidate("long", "Tarea Extra", null),   // prefijo "tarea-extra-"
        };

        // "tarea-extra-login" empieza con ambos prefijos; gana el mas largo.
        var match = Pick(candidates, "tarea-extra-login", inviter: null, evalOrg: null);

        Assert.NotNull(match);
        Assert.Equal("long", match!.Id);
    }

    [Fact]
    public void EqualLengthTie_OrgMatchWins()
    {
        var candidates = new[]
        {
            new Candidate("a", "Tarea 1", "orgA"),  // mismo slug -> misma longitud
            new Candidate("b", "Tarea 1", "orgB"),
        };

        // evalOrg null -> expectedOrg cae a Candidate.Org; el inviter "orgB"
        // coincide con el segundo, que gana el desempate de igual longitud.
        var match = Pick(candidates, "tarea-1-login", inviter: "orgB", evalOrg: null);

        Assert.NotNull(match);
        Assert.Equal("b", match!.Id);
    }

    [Fact]
    public void NoCandidateMatches_ReturnsNull()
    {
        var candidates = new[]
        {
            new Candidate("a", "Tarea 1", null),  // prefijo "tarea-1-"
        };

        var match = Pick(candidates, "otra-cosa-login", inviter: null, evalOrg: null);

        Assert.Null(match);
    }

    [Fact]
    public void EmptyRepoName_ReturnsNull()
    {
        var candidates = new[]
        {
            new Candidate("a", "Tarea 1", null),
        };

        var match = Pick(candidates, "", inviter: null, evalOrg: null);

        Assert.Null(match);
    }

    [Fact]
    public void EvalOrg_OverridesCandidateOrg()
    {
        // Con evalOrg seteado, expectedOrg = evalOrg para TODOS (ignora Candidate.Org).
        // b.Org="orgB" coincidiria con inviter "orgB" si se usara su Org (ver
        // EqualLengthTie_OrgMatchWins, que da "b"), pero evalOrg="orgA" lo
        // sobrescribe -> ningun candidato matchea el inviter -> gana el primero
        // estable, NO "b". Congela el contrato del override CurrentEvaluationOrg().
        var candidates = new[]
        {
            new Candidate("a", "Tarea 1", "orgA"),
            new Candidate("b", "Tarea 1", "orgB"),
        };

        var match = Pick(candidates, "tarea-1-login", inviter: "orgB", evalOrg: "orgA");

        Assert.NotNull(match);
        Assert.Equal("a", match!.Id);
    }

    [Fact]
    public void TotalTie_FirstStableCandidateWins()
    {
        // Mismo titulo, misma org, sin org-match: empate total -> gana el primero
        // en orden de entrada (determinismo).
        var candidates = new[]
        {
            new Candidate("first", "Tarea 1", null),
            new Candidate("second", "Tarea 1", null),
        };

        var match = Pick(candidates, "tarea-1-login", inviter: null, evalOrg: null);

        Assert.NotNull(match);
        Assert.Equal("first", match!.Id);
    }
}
