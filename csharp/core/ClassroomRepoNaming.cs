namespace EntregaEvaluacion.Core;

/// <summary>
/// Convenciones de nombre de repos de GitHub Classroom. Logica pura, sin UI.
/// </summary>
public static class ClassroomRepoNaming
{
    /// <summary>
    /// Nombre de repo esperado para una tarea de Classroom: {slug-del-titulo}-{username}.
    /// Reusa la misma normalizacion (Sanitize) que el resto de la app.
    /// </summary>
    public static string ExpectedClassroomRepo(string title, string username)
        => $"{RepoNameSanitizer.Sanitize(title)}-{username.ToLowerInvariant()}";

    /// <summary>
    /// Prefijo de slug de una tarea de Classroom: {slug-del-titulo}-. GitHub
    /// Classroom nombra el repo del alumno como {slug}-{login}, pero el login
    /// puede no coincidir byte a byte con el username GitHub (mayusculas,
    /// reclaim de cuenta). La invitacion se asocia por PREFIJO de slug, no por
    /// igualdad exacta con ExpectedClassroomRepo, para no perder invitaciones
    /// con login distinto al esperado.
    /// </summary>
    public static string ClassroomRepoPrefix(string title)
        => $"{RepoNameSanitizer.Sanitize(title)}-";
}
