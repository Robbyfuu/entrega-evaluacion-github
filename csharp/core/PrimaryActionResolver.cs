namespace EntregaEvaluacion.Core;

/// <summary>
/// Accion que el boton primario contextual debe representar segun el estado del
/// alumno. Cada valor mapea a EXACTO un (texto, apariencia, handler) en la vista;
/// por eso crear y clonar son kinds distintos (mismo handler, distinto texto) y la
/// rama "incompleto" se parte en dos (CompleteData=nuevo, SelectRepo=existente).
/// El core decide el kind; la vista (MainWindow) traduce kind -> UI/handler.
/// </summary>
public enum PrimaryActionKind
{
    /// <summary>Sin sesion: boton deshabilitado ("Inicia sesion primero").</summary>
    LoginRequired,

    /// <summary>Sesion ok, modo nuevo, faltan nombre/tipo: deshabilitado ("Completa los datos").</summary>
    CompleteData,

    /// <summary>Sesion ok, modo existente, sin repo elegido: deshabilitado ("Selecciona un repositorio").</summary>
    SelectRepo,

    /// <summary>Sesion ok, modo nuevo, datos completos: dispara CrearRepoAsync ("Crear repositorio").</summary>
    CreateRepo,

    /// <summary>Sesion ok, modo existente, repo elegido: dispara CrearRepoAsync/clona ("Clonar repositorio").</summary>
    CloneRepo,

    /// <summary>Carpeta + repo listos: dispara SubirArchivosAsync ("Subir evaluacion").</summary>
    Submit,
}

/// <summary>
/// Resultado de <see cref="PrimaryActionResolver.Resolve"/>: la accion del boton,
/// el paso del sidebar a resaltar (1/2/3) y si el boton queda habilitado.
/// <paramref name="PrimaryEnabled"/> es derivable del <paramref name="Kind"/>, pero
/// se expone explicito porque la vista lo aplica directo (y el test lo fija).
/// </summary>
public readonly record struct PrimaryActionResolution(
    PrimaryActionKind Kind, int ActiveStep, bool PrimaryEnabled);

/// <summary>
/// Decision PURA del boton primario contextual y del paso activo del sidebar,
/// extraida de MainWindow.UpdatePrimaryAction + UpdateActiveStep (ENT-7 extraction
/// #4). Sin WPF, sin Func&lt;Task&gt;, sin controles: solo el algebra de estados.
/// El caller (MainWindow) lee el estado real de la UI (sesion, carpeta, modo,
/// datos de repo), llama aqui, y mapea el <see cref="PrimaryActionKind"/> de vuelta
/// al handler y al texto/apariencia. Preserva EXACTO el orden de chequeos del
/// original (auth gate primero, luego subir, luego crear/clonar, luego incompleto).
/// </summary>
public static class PrimaryActionResolver
{
    public static PrimaryActionResolution Resolve(
        bool isAuthenticated, bool hasFolder, bool existingRepoMode, bool hasRepoData)
    {
        var kind = ResolveKind(isAuthenticated, hasFolder, existingRepoMode, hasRepoData);
        var activeStep = ResolveActiveStep(isAuthenticated, hasFolder, hasRepoData);
        // Habilitado solo en las acciones reales (crear/clonar/subir); las ramas
        // login/incompleto dejan el boton deshabilitado, igual que el original.
        var enabled = kind is PrimaryActionKind.CreateRepo
            or PrimaryActionKind.CloneRepo
            or PrimaryActionKind.Submit;
        return new PrimaryActionResolution(kind, activeStep, enabled);
    }

    private static PrimaryActionKind ResolveKind(
        bool isAuthenticated, bool hasFolder, bool existingRepoMode, bool hasRepoData)
    {
        // 1) Sin sesion: nada accionable hasta loguearse (gana sobre todo lo demas).
        if (!isAuthenticated) return PrimaryActionKind.LoginRequired;

        // 2) Carpeta lista + repo (creado o clonado) -> Subir.
        if (hasFolder && hasRepoData) return PrimaryActionKind.Submit;

        // 3) Datos del repo completos (sin carpeta aun) -> Crear / Clonar.
        if (hasRepoData)
            return existingRepoMode ? PrimaryActionKind.CloneRepo : PrimaryActionKind.CreateRepo;

        // 4) Sesion iniciada pero faltan datos del repo.
        return existingRepoMode ? PrimaryActionKind.SelectRepo : PrimaryActionKind.CompleteData;
    }

    // Paso del sidebar: sin sesion = 1; con sesion sin repo+carpeta listos = 2;
    // repo + carpeta listos = 3. Identico a UpdateActiveStep del original.
    private static int ResolveActiveStep(bool isAuthenticated, bool hasFolder, bool hasRepoData)
        => !isAuthenticated ? 1 : (hasFolder && hasRepoData ? 3 : 2);
}
