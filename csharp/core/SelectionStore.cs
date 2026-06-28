namespace EntregaEvaluacion.Core;

/// <summary>
/// Implementacion en memoria de <see cref="ISelectionStore"/>. Los tres valores
/// viven en memoria como autoritativos: en construccion se hidratan desde el
/// puerto de persistencia y cada mutacion (1) actualiza el estado en memoria,
/// (2) lo escribe a traves del puerto INMEDIATAMENTE (write-through del snapshot
/// completo) y (3) dispara <see cref="SelectionChanged"/>.
///
/// El happy path es equivalente al antiguo StudentSection. La diferencia de
/// diseno es que el store NO traga los fallos de persistencia: si el puerto
/// lanza al guardar, la excepcion se propaga (surface, not swallow). El manejo
/// tolerante a fallos (log + degradar) vive en el adaptador de persistencia
/// (RegistrySelectionPersistence), no aca, por lo que en produccion los setters
/// no lanzan. El estado en memoria se actualiza ANTES del write-through, asi que
/// sigue siendo autoritativo aunque la persistencia falle; la notificacion solo
/// se emite cuando el guardado tuvo exito.
/// </summary>
public sealed class SelectionStore : ISelectionStore
{
    private readonly ISelectionPersistence _persistence;

    private string _sectionText;
    private long? _sectionId;
    private long? _evaluationId;

    public SelectionStore(ISelectionPersistence persistence)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));

        // Hidrata el estado autoritativo desde la persistencia una sola vez.
        var snapshot = _persistence.Load();
        _sectionText = snapshot.SectionText;
        _sectionId = snapshot.SectionId;
        _evaluationId = snapshot.EvaluationId;
    }

    public string SectionText => _sectionText;
    public long? SectionId => _sectionId;
    public long? EvaluationId => _evaluationId;

    public event EventHandler? SelectionChanged;

    public void SetSectionText(string value)
    {
        _sectionText = value;
        PersistAndNotify();
    }

    public void SetSectionId(long? value)
    {
        _sectionId = value;
        PersistAndNotify();
    }

    public void SetEvaluationId(long? value)
    {
        _evaluationId = value;
        PersistAndNotify();
    }

    public void Clear()
    {
        _sectionText = SelectionSnapshot.Empty.SectionText;
        _sectionId = SelectionSnapshot.Empty.SectionId;
        _evaluationId = SelectionSnapshot.Empty.EvaluationId;
        PersistAndNotify();
    }

    // Write-through del snapshot completo y, solo si la persistencia no lanza,
    // notificacion del cambio. Persistir antes de notificar evita anunciar un
    // cambio que no se pudo guardar.
    private void PersistAndNotify()
    {
        _persistence.Save(new SelectionSnapshot(_sectionText, _sectionId, _evaluationId));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
