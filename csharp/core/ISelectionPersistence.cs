namespace EntregaEvaluacion.Core;

/// <summary>
/// Puerto de persistencia de la seleccion del alumno. Aisla a
/// <see cref="SelectionStore"/> del almacenamiento concreto (HKCU en
/// produccion) para que la logica del store sea testeable con un doble en
/// memoria. La implementacion debe degradar con gracia: <see cref="Load"/>
/// devuelve <see cref="SelectionSnapshot.Empty"/> si no hay datos o la lectura
/// falla, en vez de lanzar.
/// </summary>
public interface ISelectionPersistence
{
    // Carga la seleccion persistida (Empty si no hay nada o la lectura falla).
    SelectionSnapshot Load();

    // Persiste la seleccion completa (los tres valores como una unidad).
    void Save(SelectionSnapshot snapshot);
}
