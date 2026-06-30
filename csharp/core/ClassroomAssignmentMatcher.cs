namespace EntregaEvaluacion.Core;

/// <summary>
/// Asociacion repo -> tarea para el registro de aceptaciones y entregas. Logica
/// pura, sin UI ni I/O. Generico sobre el tipo de candidato: el caller provee
/// como obtener el titulo de cada tarea, asi el algoritmo no depende de ningun
/// DTO concreto.
///
/// Match EXACTO por nombre esperado de Classroom ({slug}-{username} via
/// ClassroomRepoNaming.ExpectedClassroomRepo), case-insensitive contra el
/// repoName. Gana el PRIMER candidato en orden de entrada (estable). Si NO hay
/// match y singleActiveFallback es true y hay EXACTAMENTE una tarea, se devuelve
/// esa (los slugs de Classroom no siempre coinciden con Sanitize(titulo)); en
/// cualquier otro caso sin match devuelve null. Determinista.
/// </summary>
public static class ClassroomAssignmentMatcher
{
    public static T? MatchByExpectedRepo<T>(
        IReadOnlyList<T> candidates, string repoName, string username,
        Func<T, string> titleOf, bool singleActiveFallback) where T : class
    {
        foreach (var a in candidates)
        {
            if (string.Equals(
                    ClassroomRepoNaming.ExpectedClassroomRepo(titleOf(a), username),
                    repoName, StringComparison.OrdinalIgnoreCase))
                return a;
        }

        if (singleActiveFallback && candidates.Count == 1) return candidates[0];
        return null;
    }
}
