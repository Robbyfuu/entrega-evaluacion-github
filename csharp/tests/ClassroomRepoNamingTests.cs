using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests: congelan el comportamiento ACTUAL de las
/// convenciones de nombre de repos de Classroom. Salidas capturadas del
/// metodo real.
/// </summary>
public class ClassroomRepoNamingTests
{
    [Theory]
    // Caso tipico: slug del titulo + '-' + username en minusculas.
    [InlineData("Tarea 1", "JuanPerez", "tarea-1-juanperez")]
    // El titulo se sanitiza; el username solo se pasa a minusculas (el punto
    // NO se sanitiza, queda tal cual).
    [InlineData("Evaluación Práctica", "Maria.Lopez", "evaluacion-practica-maria.lopez")]
    // OJO comportamiento actual: el username NO se sanitiza, solo ToLower.
    // Guion bajo y tilde del username se conservan.
    [InlineData("Lab Final", "Ana_Pérez99", "lab-final-ana_pérez99")]
    // Titulo vacio -> el slug queda vacio y el nombre arranca con '-'.
    [InlineData("", "carlos", "-carlos")]
    // Simbolos del titulo se eliminan; username conserva su guion interno.
    [InlineData("¿Examen?", "User-Name", "examen-user-name")]
    public void ExpectedClassroomRepo_ProducesExpectedName(string title, string username, string expected)
    {
        Assert.Equal(expected, ClassroomRepoNaming.ExpectedClassroomRepo(title, username));
    }

    [Theory]
    // Prefijo = slug del titulo + '-'.
    [InlineData("Tarea 1", "tarea-1-")]
    [InlineData("Evaluación Práctica", "evaluacion-practica-")]
    // Titulo vacio -> solo el guion separador.
    [InlineData("", "-")]
    [InlineData("¡Hola, Mundo!", "hola-mundo-")]
    public void ClassroomRepoPrefix_ProducesExpectedPrefix(string title, string expected)
    {
        Assert.Equal(expected, ClassroomRepoNaming.ClassroomRepoPrefix(title));
    }
}
