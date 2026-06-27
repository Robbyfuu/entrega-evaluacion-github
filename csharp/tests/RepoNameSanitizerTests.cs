using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests: congelan el comportamiento ACTUAL de Sanitize.
/// Las salidas esperadas fueron capturadas ejecutando el metodo real, no
/// idealizadas. Si un cambio futuro las rompe, es un cambio de comportamiento.
/// </summary>
public class RepoNameSanitizerTests
{
    [Theory]
    // Tildes: se quitan (Normalization FormD + descarte de NonSpacingMark).
    [InlineData("Programación Avanzada", "programacion-avanzada")]
    // Espacios al borde y repetidos colapsan a un guion y se recortan.
    [InlineData("  Hola   Mundo  ", "hola-mundo")]
    // Mayusculas pasan a minusculas.
    [InlineData("MAYÚSCULAS", "mayusculas")]
    // Simbolos no [a-z0-9-] se eliminan; resto colapsa a guiones simples.
    [InlineData("Tarea #1: ¡Évalúa!", "tarea-1-evalua")]
    // Guiones multiples colapsan a uno solo.
    [InlineData("A -- B", "a-b")]
    // Solo simbolos -> cadena vacia.
    [InlineData("@#$%", "")]
    // Cadena vacia -> cadena vacia.
    [InlineData("", "")]
    // enie y mas tildes: se conserva la base ASCII.
    [InlineData("Diseño Ágil ñandú", "diseno-agil-nandu")]
    // '+' y '#' se eliminan; cada hueco queda como separador colapsado.
    [InlineData("C++ y C#", "c-y-c")]
    // Numeros se conservan.
    [InlineData("Lab 2025 v3", "lab-2025-v3")]
    public void Sanitize_ProducesExpectedSlug(string input, string expected)
    {
        Assert.Equal(expected, RepoNameSanitizer.Sanitize(input));
    }
}
