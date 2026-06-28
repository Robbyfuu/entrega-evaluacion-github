using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests de la decision PURA del boton primario contextual y del
/// paso activo del sidebar, extraida de MainWindow.UpdatePrimaryAction +
/// UpdateActiveStep hacia <see cref="PrimaryActionResolver"/>.
///
/// Congelan el mapeo (estado -> accion, paso, habilitado) preservando EXACTO el
/// orden de chequeos del original:
///   1) sin sesion  -> login (gana sobre todo lo demas, paso 1, deshabilitado)
///   2) carpeta + repo listos -> subir (paso 3, habilitado)
///   3) datos de repo (sin carpeta) -> crear/clonar (paso 2, habilitado)
///   4) sesion ok sin datos de repo -> incompleto (paso 2, deshabilitado)
/// El texto/apariencia/handler concretos viven en la vista; el core solo decide el
/// <see cref="PrimaryActionKind"/>, el paso y si el boton queda habilitado.
/// </summary>
public class PrimaryActionResolverTests
{
    [Theory]
    // isAuth, hasFolder, existingMode, hasRepoData -> kind, activeStep, enabled

    // --- Sin sesion: el gate de auth gana sobre cualquier otro estado ---
    [InlineData(false, false, false, false, PrimaryActionKind.LoginRequired, 1, false)]
    [InlineData(false, true,  true,  true,  PrimaryActionKind.LoginRequired, 1, false)]

    // --- Con sesion, sin datos de repo: incompleto (paso 2, deshabilitado).
    //     El texto depende del modo (CompleteData=nuevo, SelectRepo=existente);
    //     tener carpeta no alcanza para avanzar sin datos de repo. ---
    [InlineData(true,  false, false, false, PrimaryActionKind.CompleteData, 2, false)]
    [InlineData(true,  true,  false, false, PrimaryActionKind.CompleteData, 2, false)]
    [InlineData(true,  false, true,  false, PrimaryActionKind.SelectRepo,   2, false)]
    [InlineData(true,  true,  true,  false, PrimaryActionKind.SelectRepo,   2, false)]

    // --- Con sesion, datos de repo, sin carpeta: crear (nuevo) / clonar (existente) ---
    [InlineData(true,  false, false, true,  PrimaryActionKind.CreateRepo, 2, true)]
    [InlineData(true,  false, true,  true,  PrimaryActionKind.CloneRepo,  2, true)]

    // --- Con sesion, datos de repo + carpeta: subir (paso 3, habilitado) ---
    [InlineData(true,  true,  false, true,  PrimaryActionKind.Submit, 3, true)]
    [InlineData(true,  true,  true,  true,  PrimaryActionKind.Submit, 3, true)]
    public void Resolve_MapsStateToActionStepAndEnabled(
        bool isAuth, bool hasFolder, bool existingMode, bool hasRepoData,
        PrimaryActionKind expectedKind, int expectedStep, bool expectedEnabled)
    {
        var r = PrimaryActionResolver.Resolve(isAuth, hasFolder, existingMode, hasRepoData);

        Assert.Equal(expectedKind, r.Kind);
        Assert.Equal(expectedStep, r.ActiveStep);
        Assert.Equal(expectedEnabled, r.PrimaryEnabled);
    }

    [Fact]
    public void Resolve_NotAuthenticated_WinsOverEverythingElse()
    {
        // Aunque carpeta + repo + modo esten listos, sin sesion el boton es login.
        var r = PrimaryActionResolver.Resolve(
            isAuthenticated: false, hasFolder: true, existingRepoMode: false, hasRepoData: true);

        Assert.Equal(PrimaryActionKind.LoginRequired, r.Kind);
        Assert.Equal(1, r.ActiveStep);
        Assert.False(r.PrimaryEnabled);
    }

    [Fact]
    public void Resolve_DistinguishesCreateFromClone_BySameStepAndEnabled()
    {
        // Crear y clonar comparten paso (2) y habilitado (true); el modo solo cambia
        // el kind (la vista mapea ambos al MISMO handler CrearRepoAsync).
        var create = PrimaryActionResolver.Resolve(true, false, existingRepoMode: false, hasRepoData: true);
        var clone = PrimaryActionResolver.Resolve(true, false, existingRepoMode: true, hasRepoData: true);

        Assert.Equal(PrimaryActionKind.CreateRepo, create.Kind);
        Assert.Equal(PrimaryActionKind.CloneRepo, clone.Kind);
        Assert.Equal(create.ActiveStep, clone.ActiveStep);
        Assert.Equal(create.PrimaryEnabled, clone.PrimaryEnabled);
    }
}
