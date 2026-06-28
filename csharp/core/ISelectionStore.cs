namespace EntregaEvaluacion.Core;

/// <summary>
/// Estado de seleccion del alumno (seccion + evaluacion) como dependencia
/// inyectable, reemplazo de la clase estatica global StudentSection. Mantiene
/// los tres valores en memoria como autoritativos y los escribe a traves del
/// puerto de persistencia en cada cambio. Notifica cada mutacion efectiva via
/// <see cref="SelectionChanged"/>. Envuelve EXACTAMENTE lo que persistia
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

    // Fija el codigo de seccion, persiste y notifica.
    void SetSectionText(string value);

    // Fija el section_id (o lo limpia con null), persiste y notifica.
    void SetSectionId(long? value);

    // Fija el evaluation_id (o lo limpia con null), persiste y notifica.
    void SetEvaluationId(long? value);

    // Resetea los tres valores a su default, persiste y notifica.
    void Clear();

    // Se dispara tras cada mutacion efectiva (cada setter y Clear).
    event EventHandler? SelectionChanged;
}
