using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Golden / characterization tests para la normalizacion de procesos y la lista
/// fallback. Los vectores entrada->salida DEBEN coincidir con
/// admin-next/lib/suspicious.test.ts (paridad cross-runtime C#/TS): si divergen,
/// una de las dos implementaciones rompio el contrato. Las salidas esperadas se
/// capturaron ejecutando el metodo real, no se idealizaron.
/// </summary>
public class SuspiciousProcessesTests
{
    [Theory]
    [InlineData("Chrome.exe", "chrome")]                          // mayusculas + sufijo .exe
    [InlineData("CHROME.EXE", "chrome")]                          // .EXE en mayusculas (lower primero)
    [InlineData("  CODE  ", "code")]                              // espacios al borde + mayusculas
    [InlineData(" Telegram.exe ", "telegram")]                    // espacios + .exe
    [InlineData("notepad++", "notepad++")]                        // ya normalizado, simbolos preservados
    [InlineData("chrome", "chrome")]                              // ya normalizado
    [InlineData("C:\\Tools\\AnyDesk.exe", "c:\\tools\\anydesk")]  // ruta/path Windows
    [InlineData("my app .exe", "my app")]                         // espacio antes de .exe (trim final)
    [InlineData(".exe", "")]                                      // solo el sufijo -> vacio
    [InlineData("", "")]                                          // vacio
    [InlineData("   ", "")]                                       // solo whitespace
    // No-ASCII: latin acentuado concuerda byte-a-byte entre ToLowerInvariant (.NET)
    // y toLowerCase (JS). NOTA: caracteres patologicos como U+0130 (I turca) o
    // U+1E9E (ss) SI divergen entre runtimes; quedan FUERA de contrato porque los
    // nombres de proceso reales son ASCII/latin. Si algun dia un proceso usa esos
    // chars, este golden test forzaria una decision explicita de contrato.
    [InlineData("Café.EXE", "café")]                              // acento preservado + .exe
    public void Normalize_ProducesExpectedOutput(string input, string expected)
    {
        Assert.Equal(expected, SuspiciousProcesses.Normalize(input));
    }

    [Fact]
    public void Fallback_HasExactContract()
    {
        // Cantidad exacta: cualquier alta/baja debe replicarse en TS y en el seed SQL.
        Assert.Equal(34, SuspiciousProcesses.Fallback.Length);

        // Entradas clave presentes (mismas verificadas en suspicious.test.ts).
        Assert.Contains("chrome", SuspiciousProcesses.Fallback);
        Assert.Contains("copilot", SuspiciousProcesses.Fallback);
        Assert.Contains("notepad++", SuspiciousProcesses.Fallback);
        Assert.Contains("claude", SuspiciousProcesses.Fallback);
    }
}
