using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests de las 4 decisiones PURAS de la cascada de seleccion
/// (curso > seccion > evaluacion), extraidas de MainWindow.InitAsync /
/// PopulateSectionCombo / ApplyEvaluationLock / LoadEvaluationsForSection hacia
/// <see cref="SelectionCascadeResolver"/>.
///
/// El core es generico (proyecciones via lambdas) para no arrastrar los DTOs WPF
/// (SectionRow, Evaluation, AssignmentStatus); aqui se usan records simples como
/// dobles. Congelan los invariantes:
///   1) seccion guardada: SectionId-first, Code-fallback.
///   2) filtro de secciones por curso (+ fallback legacy cuando no hay secciones).
///   3) lock de evaluacion: Accepted &amp;&amp; !Submitted &amp;&amp; EvaluationId&gt;0.
///   4) sintesis Id=0 SOLO cuando sectionId==null (vacio-en-vivo es intencional).
/// </summary>
public class SelectionCascadeResolverTests
{
    private sealed record Sec(long Id, long CourseId, string Code);
    private sealed record Ev(long Id, string Title);
    private sealed record Stat(bool Accepted, bool Submitted, long? EvaluationId);

    private static Sec? ResolveSaved(string? savedCode, long? savedSectionId, IReadOnlyList<Sec> sections)
        => SelectionCascadeResolver.ResolveSavedSection(
            savedCode, savedSectionId, sections, s => s.Id, s => s.Code);

    private static IReadOnlyList<string> ResolveCodes(long? courseId, IReadOnlyList<Sec> sections, IReadOnlyList<string> fallback)
        => SelectionCascadeResolver.ResolveSectionCodes(
            courseId, sections, fallback, s => s.CourseId, s => s.Code);

    private static long? ResolveLocked(IEnumerable<Stat> statuses)
        => SelectionCascadeResolver.ResolveLockedEvaluationId(
            statuses, s => s.Accepted, s => s.Submitted, s => s.EvaluationId);

    private static IReadOnlyList<Ev> ResolveEvals(long? sectionId, IReadOnlyList<Ev> fetched, IReadOnlyList<string> fallback)
        => SelectionCascadeResolver.ResolveEvaluationsToShow(
            sectionId, fetched, fallback, t => new Ev(0, t));

    // ===================== 1) Seccion guardada =====================

    [Fact]
    public void ResolveSavedSection_SectionIdMatchWinsOverCodeFallback()
    {
        // El Id apunta a una fila; el Code guardado apunta a OTRA. Debe ganar el
        // match por Id (identidad real), no por code (puede repetirse entre cursos).
        var sections = new[]
        {
            new Sec(Id: 1, CourseId: 10, Code: "002D"), // match por Id
            new Sec(Id: 2, CourseId: 20, Code: "001D"), // match por Code
        };

        var row = ResolveSaved(savedCode: "001D", savedSectionId: 1, sections);

        Assert.NotNull(row);
        Assert.Equal(1, row!.Id);
        Assert.Equal("002D", row.Code);
    }

    [Fact]
    public void ResolveSavedSection_FallsBackToCode_WhenSectionIdHasNoMatch()
    {
        var sections = new[]
        {
            new Sec(Id: 1, CourseId: 10, Code: "002D"),
            new Sec(Id: 2, CourseId: 20, Code: "001D"),
        };

        // Id 99 no existe -> cae a buscar por code "001D".
        var row = ResolveSaved(savedCode: "001D", savedSectionId: 99, sections);

        Assert.NotNull(row);
        Assert.Equal(2, row!.Id);
    }

    [Fact]
    public void ResolveSavedSection_UsesCode_WhenSectionIdNull()
    {
        var sections = new[]
        {
            new Sec(Id: 1, CourseId: 10, Code: "002D"),
            new Sec(Id: 2, CourseId: 20, Code: "001D"),
        };

        var row = ResolveSaved(savedCode: "001D", savedSectionId: null, sections);

        Assert.NotNull(row);
        Assert.Equal(2, row!.Id);
    }

    [Fact]
    public void ResolveSavedSection_ReturnsNull_WhenNeitherMatches()
    {
        var sections = new[] { new Sec(Id: 1, CourseId: 10, Code: "002D") };

        Assert.Null(ResolveSaved(savedCode: "999X", savedSectionId: 99, sections));
    }

    // ===================== 2) Filtro de secciones por curso =====================

    [Fact]
    public void ResolveSectionCodes_EmptySections_ReturnsLegacyFallback()
    {
        var fallback = new[] { "001D", "002D", "003D" };

        var codes = ResolveCodes(courseId: 10, sections: Array.Empty<Sec>(), fallback);

        Assert.Equal(fallback, codes);
    }

    [Fact]
    public void ResolveSectionCodes_FiltersByCourse_PreservingOrder()
    {
        var sections = new[]
        {
            new Sec(Id: 1, CourseId: 10, Code: "001D"),
            new Sec(Id: 2, CourseId: 20, Code: "002D"),
            new Sec(Id: 3, CourseId: 10, Code: "003D"),
        };

        var codes = ResolveCodes(courseId: 10, sections, fallback: Array.Empty<string>());

        Assert.Equal(new[] { "001D", "003D" }, codes);
    }

    [Fact]
    public void ResolveSectionCodes_NullCourse_ReturnsAllCodes()
    {
        var sections = new[]
        {
            new Sec(Id: 1, CourseId: 10, Code: "001D"),
            new Sec(Id: 2, CourseId: 20, Code: "002D"),
        };

        var codes = ResolveCodes(courseId: null, sections, fallback: Array.Empty<string>());

        Assert.Equal(new[] { "001D", "002D" }, codes);
    }

    // ===================== 3) Lock de evaluacion =====================

    [Theory]
    // accepted, submitted, evalId -> expected locked id
    [InlineData(true, false, 5L, 5L)]      // aceptada, no entregada, id>0 -> bloquea
    [InlineData(true, true, 5L, null)]     // ya entregada -> no bloquea
    [InlineData(false, false, 5L, null)]   // no aceptada -> no bloquea
    [InlineData(true, false, 0L, null)]    // evaluationId=0 (sentinel) -> no bloquea
    [InlineData(true, false, null, null)]  // sin evaluationId -> no bloquea
    public void ResolveLockedEvaluationId_AppliesAcceptedNotSubmittedPositiveIdRule(
        bool accepted, bool submitted, long? evalId, long? expected)
    {
        var statuses = new[] { new Stat(accepted, submitted, evalId) };

        Assert.Equal(expected, ResolveLocked(statuses));
    }

    [Fact]
    public void ResolveLockedEvaluationId_ReturnsFirstMatch()
    {
        var statuses = new[]
        {
            new Stat(Accepted: true, Submitted: true, EvaluationId: 7), // descartada (entregada)
            new Stat(Accepted: true, Submitted: false, EvaluationId: 8), // primer match
            new Stat(Accepted: true, Submitted: false, EvaluationId: 9),
        };

        Assert.Equal(8, ResolveLocked(statuses));
    }

    [Fact]
    public void ResolveLockedEvaluationId_EmptyList_ReturnsNull()
    {
        Assert.Null(ResolveLocked(Array.Empty<Stat>()));
    }

    // ===================== 4) Sintesis Id=0 solo cuando sectionId==null =====================

    [Fact]
    public void ResolveEvaluationsToShow_FetchedNonEmpty_ReturnsFetchedAsIs()
    {
        var fetched = new[] { new Ev(11, "Evaluacion-1"), new Ev(12, "Evaluacion-2") };
        var fallback = new[] { "X", "Y" };

        // Aunque sectionId sea null, si hay fetcheadas se usan tal cual (no sintetiza).
        var shown = ResolveEvals(sectionId: null, fetched, fallback);

        Assert.Same(fetched, shown);
    }

    [Fact]
    public void ResolveEvaluationsToShow_EmptyAndNullSection_SynthesizesFallbackWithIdZero()
    {
        var fallback = new[] { "Evaluacion-1", "Examen" };

        var shown = ResolveEvals(sectionId: null, fetched: Array.Empty<Ev>(), fallback);

        Assert.Collection(shown,
            e => { Assert.Equal(0, e.Id); Assert.Equal("Evaluacion-1", e.Title); },
            e => { Assert.Equal(0, e.Id); Assert.Equal("Examen", e.Title); });
    }

    [Fact]
    public void ResolveEvaluationsToShow_EmptyButLiveSection_ReturnsEmpty_NoSynthesis()
    {
        var fallback = new[] { "Evaluacion-1", "Examen" };

        // sectionId != null con fetch vacio: el profe no activo evaluaciones.
        // Intencional: el combo queda vacio, NO se inventan opciones de fallback.
        var shown = ResolveEvals(sectionId: 42, fetched: Array.Empty<Ev>(), fallback);

        Assert.Empty(shown);
    }
}
