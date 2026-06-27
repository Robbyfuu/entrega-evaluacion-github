using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Characterization tests: congelan el comportamiento ACTUAL de la
/// clasificacion de logs extraida de MainWindow.ClassifyLog. Un caso por
/// categoria mas el caso que no matchea ninguna (null).
/// </summary>
public class LogClassifierTests
{
    [Theory]
    // Contiene "error".
    [InlineData("Ocurrio un error al subir el repo")]
    // Empieza con "no se".
    [InlineData("No se pudo verificar invitaciones")]
    public void Classify_ErrorMessages_ReturnError(string msg)
    {
        Assert.Equal(ToastKind.Error, LogClassifier.Classify(msg));
    }

    [Theory]
    // Empieza con "ok ".
    [InlineData("OK login completado")]
    // Contiene "creado".
    [InlineData("Repositorio creado")]
    public void Classify_SuccessMessages_ReturnSuccess(string msg)
    {
        Assert.Equal(ToastKind.Success, LogClassifier.Classify(msg));
    }

    [Fact]
    public void Classify_AdminMessage_ReturnsInfo()
    {
        // Empieza con "[admin]".
        Assert.Equal(ToastKind.Info, LogClassifier.Classify("[admin] config actualizada"));
    }

    [Fact]
    public void Classify_RoutineMessage_ReturnsNull()
    {
        // No matchea ninguna categoria -> solo barra de estado.
        Assert.Null(LogClassifier.Classify("Subiendo archivos al repositorio"));
    }
}
