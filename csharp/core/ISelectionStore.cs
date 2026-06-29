namespace EntregaEvaluacion.Core;

/// <summary>
/// Estado de seleccion del alumno (seccion + evaluacion) como dependencia
/// inyectable, reemplazo de la clase estatica global StudentSection. Mantiene
/// los tres valores en memoria como autoritativos y los escribe a traves del
/// puerto de persistencia en cada cambio. Cada setter y <see cref="Clear"/>
/// persisten el snapshot completo y, SOLO si el guardado tuvo exito, notifican
/// via <see cref="SelectionChanged"/>. Si la persistencia LANZA, la excepcion se
/// PROPAGA (surface, not swallow) y NO se emite la notificacion; el manejo
/// tolerante a fallos vive en el adaptador de persistencia (p. ej.
/// RegistrySelectionPersistence), por lo que en produccion los setters no lanzan.
/// Envuelve EXACTAMENTE lo que persistia
/// StudentSection: SectionText, SectionId y EvaluationId (no CourseId ni la
/// evaluacion en memoria de la UI).
/// </summary>
public interface ISelectionStore
{
    // Codigo de seccion (TEXT). Nunca null: "" cuando no hay seleccion.
    string SectionText { get; }

    // Identidad real de la seccion (section_id); null si no hay.
    long? SectionId { get; }

    // Evaluacion seleccionada (evaluation_id); null si no hay.
    long? EvaluationId { get; }

    // Fija el codigo de seccion: persiste el snapshot y, si el guardado tuvo
    // exito, notifica. Puede LANZAR si la persistencia falla (surface, not swallow).
    void SetSectionText(string value);

    // Fija el section_id (o lo limpia con null): persiste y, si tuvo exito,
    // notifica. Puede LANZAR si la persistencia falla.
    void SetSectionId(long? value);

    // Fija el evaluation_id (o lo limpia con null): persiste y, si tuvo exito,
    // notifica. Puede LANZAR si la persistencia falla.
    void SetEvaluationId(long? value);

    // Resetea los tres valores a su default: persiste y, si tuvo exito, notifica.
    // Puede LANZAR si la persistencia falla.
    void Clear();

    // Se dispara tras cada mutacion efectiva (cada setter y Clear) SOLO cuando el
    // guardado en persistencia tuvo exito.
    event EventHandler? SelectionChanged;
}
