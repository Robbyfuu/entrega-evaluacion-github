using System.Collections.Generic;
using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests: congelan el algoritmo de asociacion repo -> tarea que
/// hoy vive inline en RecordAcceptanceIfClassroomRepoAsync (sin fallback) y
/// RecordSubmissionIfClassroomRepoAsync (con fallback a la unica tarea activa).
/// Match exacto por ExpectedClassroomRepo(title, username), case-insensitive en
/// el repoName; primer match en orden de entrada gana; fallback opcional cuando
/// no hay match y hay EXACTAMENTE una tarea.
/// </summary>
public class ClassroomAssignmentMatcherTests
{
    private sealed record Asg(long Id, string Title);

    private static IReadOnlyList<Asg> List(params Asg[] a) => a;

    [Fact]
    public void Match_ByExpectedRepo_ReturnsMatchingAssignment()
    {
        var asg = List(new Asg(1, "Tarea 1"), new Asg(2, "Tarea 2"));
        var m = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, "tarea-2-juan", "juan", a => a.Title, singleActiveFallback: false);
        Assert.NotNull(m);
        Assert.Equal(2, m!.Id);
    }

    [Fact]
    public void Match_IsCaseInsensitive_OnRepoName()
    {
        var asg = List(new Asg(1, "Tarea 1"));
        var m = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, "TAREA-1-JUAN", "juan", a => a.Title, singleActiveFallback: false);
        Assert.NotNull(m);
        Assert.Equal(1, m!.Id);
    }

    [Fact]
    public void Match_FirstWins_WhenMultipleExpectedNamesEqual()
    {
        // Titulos duplicados => mismo nombre esperado; gana el primero en orden.
        var asg = List(new Asg(10, "Tarea"), new Asg(20, "Tarea"));
        var m = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, "tarea-juan", "juan", a => a.Title, singleActiveFallback: false);
        Assert.NotNull(m);
        Assert.Equal(10, m!.Id);
    }

    [Fact]
    public void NoMatch_NoFallback_ReturnsNull()
    {
        var asg = List(new Asg(1, "Tarea 1"), new Asg(2, "Tarea 2"));
        var m = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, "otro-repo-juan", "juan", a => a.Title, singleActiveFallback: false);
        Assert.Null(m);
    }

    [Fact]
    public void NoMatch_WithFallback_SingleActive_ReturnsThatOne()
    {
        var asg = List(new Asg(99, "Tarea Distinta"));
        var m = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, "nombre-que-no-coincide", "juan", a => a.Title, singleActiveFallback: true);
        Assert.NotNull(m);
        Assert.Equal(99, m!.Id);
    }

    [Fact]
    public void NoMatch_WithFallback_MultipleActive_ReturnsNull()
    {
        var asg = List(new Asg(1, "A"), new Asg(2, "B"));
        var m = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, "no-match", "juan", a => a.Title, singleActiveFallback: true);
        Assert.Null(m);
    }

    [Fact]
    public void Empty_WithFallback_ReturnsNull()
    {
        var asg = List();
        var m = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, "x", "juan", a => a.Title, singleActiveFallback: true);
        Assert.Null(m);
    }

    [Fact]
    public void Match_TakesPrecedence_OverSingleActiveFallback()
    {
        var asg = List(new Asg(7, "Tarea 1"));
        var m = ClassroomAssignmentMatcher.MatchByExpectedRepo(
            asg, "tarea-1-juan", "juan", a => a.Title, singleActiveFallback: true);
        Assert.NotNull(m);
        Assert.Equal(7, m!.Id);
    }
}
