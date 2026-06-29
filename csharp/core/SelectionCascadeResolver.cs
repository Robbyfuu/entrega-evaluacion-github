namespace EntregaEvaluacion.Core;

/// <summary>
/// Decisiones PURAS de la cascada de seleccion del alumno (curso &gt; seccion &gt;
/// evaluacion), extraidas de MainWindow (ENT-8 slice D, HYBRID). Sin WPF, sin I/O,
/// sin estado: solo el algebra de resolucion. El caller (MainWindow) hace los
/// fetches y la manipulacion de controles (Items/SelectedIndex/IsEnabled/ToolTip),
/// conserva los guards de reentrada (_initializing/_syncingTipo) y aplica las
/// decisiones; aqui vive UNICAMENTE el "que" decidir, no el "como" aplicarlo.
///
/// Generico (proyecciones via lambdas, mismo patron que
/// <see cref="ClassroomRepoMatcher"/>) para no arrastrar los DTOs WPF
/// (Models.SectionRow / Models.Evaluation / Models.AssignmentStatus) a la capa
/// Core, que es cross-platform y testeable sin la UI.
/// </summary>
public static class SelectionCascadeResolver
{
    /// <summary>
    /// Resuelve la seccion guardada contra las secciones fetcheadas. Espeja EXACTO
    /// la precedencia del original (MainWindow.InitAsync): primero match por
    /// <paramref name="savedSectionId"/> (identidad real), y solo si no existe cae a
    /// buscar por <paramref name="savedCode"/> (codigo, que puede repetirse entre
    /// cursos). Devuelve la fila a restaurar, o <c>null</c> si ninguna matchea.
    /// </summary>
    public static TSection? ResolveSavedSection<TSection>(
        string? savedCode,
        long? savedSectionId,
        IReadOnlyList<TSection> sections,
        Func<TSection, long> idOf,
        Func<TSection, string> codeOf) where TSection : class
    {
        var savedRow = savedSectionId.HasValue
            ? sections.FirstOrDefault(s => idOf(s) == savedSectionId.Value)
            : null;
        savedRow ??= sections.FirstOrDefault(s => codeOf(s) == savedCode);
        return savedRow;
    }

    /// <summary>
    /// Codigos de seccion a mostrar (MainWindow.PopulateSectionCombo). Si no hay
    /// secciones fetcheadas cae a <paramref name="fallbackSections"/> (Config.Sections,
    /// fallback legacy). Si las hay, filtra por <paramref name="courseId"/> cuando
    /// viene dado (si no, devuelve todas) y proyecta a sus codigos, preservando el
    /// orden de entrada. Devuelve los strings; el caller hace Items.Clear()/Add().
    /// </summary>
    public static IReadOnlyList<string> ResolveSectionCodes<TSection>(
        long? courseId,
        IReadOnlyList<TSection> sections,
        IReadOnlyList<string> fallbackSections,
        Func<TSection, long> courseIdOf,
        Func<TSection, string> codeOf)
    {
        if (sections.Count == 0)
            return fallbackSections;

        var filtered = courseId is { } cid
            ? sections.Where(s => courseIdOf(s) == cid)
            : sections;
        return filtered.Select(codeOf).ToList();
    }

    /// <summary>
    /// Id de la evaluacion a BLOQUEAR (MainWindow.ApplyEvaluationLock): la primera
    /// tarea Aceptada y NO entregada con un EvaluationId real (&gt;0). Devuelve ese
    /// id, o <c>null</c> si no hay ninguna (= liberar el lock). El caller aplica la
    /// decision (fijar EvaluationCombo.SelectedIndex con el guard <c>!= i</c> y
    /// deshabilitar los combos); aqui solo se resuelve CUAL evaluacion.
    /// </summary>
    public static long? ResolveLockedEvaluationId<TStatus>(
        IEnumerable<TStatus> statuses,
        Func<TStatus, bool> acceptedOf,
        Func<TStatus, bool> submittedOf,
        Func<TStatus, long?> evaluationIdOf) where TStatus : class
    {
        var locked = statuses.FirstOrDefault(s =>
            acceptedOf(s) && !submittedOf(s)
            && evaluationIdOf(s) is { } id && id > 0);
        return locked is null ? null : evaluationIdOf(locked);
    }

    /// <summary>
    /// Lista de evaluaciones a mostrar (MainWindow.LoadEvaluationsForSection),
    /// distinguiendo los dos casos del original:
    /// <list type="bullet">
    ///   <item>Hay fetcheadas (<paramref name="fetched"/> no vacio): se usan tal cual,
    ///   sin importar <paramref name="sectionId"/>.</item>
    ///   <item>Vacio y <paramref name="sectionId"/> == null (sin BD / modo legacy):
    ///   sintetiza desde <paramref name="fallbackTypes"/> (Config.EvaluationTypes)
    ///   con <paramref name="makeFallback"/> (Id=0 = sentinel de fallback).</item>
    ///   <item>Vacio y <paramref name="sectionId"/> != null (BD viva): lista vacia.
    ///   El profe no activo evaluaciones; NO se inventan opciones (vacio-en-vivo es
    ///   intencional).</item>
    /// </list>
    /// El caller construye los items de fallback (asi Core no referencia el DTO
    /// Evaluation) y hace Items.Clear()/Add(); tambien conserva
    /// <c>_currentEvaluations</c> = <paramref name="fetched"/> (no la lista sintetizada).
    /// </summary>
    public static IReadOnlyList<TEval> ResolveEvaluationsToShow<TEval>(
        long? sectionId,
        IReadOnlyList<TEval> fetched,
        IReadOnlyList<string> fallbackTypes,
        Func<string, TEval> makeFallback)
    {
        if (fetched.Count > 0)
            return fetched;
        if (sectionId == null)
            return fallbackTypes.Select(makeFallback).ToList();
        return Array.Empty<TEval>();
    }
}
