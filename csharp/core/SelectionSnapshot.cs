namespace EntregaEvaluacion.Core;

/// <summary>
/// Snapshot inmutable de la seleccion del alumno que se persiste: el codigo de
/// seccion (TEXT, coexistencia con clientes viejos) mas section_id y
/// evaluation_id (multi-evaluacion). Es la unidad que cruza el puerto
/// <see cref="ISelectionPersistence"/> al cargar y guardar.
/// </summary>
public sealed record SelectionSnapshot(string SectionText, long? SectionId, long? EvaluationId)
{
    /// <summary>
    /// Seleccion vacia (sin seccion ni ids): el estado por defecto y el valor
    /// degradado que la persistencia devuelve cuando no hay datos o la lectura
    /// falla (espeja el "" / null que devolvia StudentSection).
    /// </summary>
    public static SelectionSnapshot Empty { get; } = new("", null, null);
}
