using EntregaEvaluacion.Core;
using Xunit;

namespace EntregaEvaluacion.Tests;

/// <summary>
/// Tests de la LOGICA de SelectionStore (la primera costura con logica real del
/// desacople de MainWindow). Usa un doble en memoria de ISelectionPersistence
/// para verificar hidratacion en construccion, write-through, notificacion y
/// reseteo, sin tocar el registro de Windows.
/// </summary>
public class SelectionStoreTests
{
    // Doble en memoria del puerto de persistencia: registra las llamadas a Save
    // y sirve el snapshot configurado en Load. Opcionalmente lanza en Save para
    // ejercer el contrato de "surface, not swallow".
    private sealed class FakePersistence : ISelectionPersistence
    {
        public SelectionSnapshot Stored { get; set; } = SelectionSnapshot.Empty;
        public int LoadCount { get; private set; }
        public List<SelectionSnapshot> Saved { get; } = new();
        public bool ThrowOnSave { get; set; }

        public SelectionSnapshot Load()
        {
            LoadCount++;
            return Stored;
        }

        public void Save(SelectionSnapshot snapshot)
        {
            if (ThrowOnSave)
                throw new InvalidOperationException("persistencia no disponible");
            Saved.Add(snapshot);
        }
    }

    // ===== Hidratacion en construccion =====

    [Fact]
    public void Constructor_HydratesStateFromPersistenceLoad()
    {
        var fake = new FakePersistence { Stored = new SelectionSnapshot("003D", 42, 7) };

        var store = new SelectionStore(fake);

        Assert.Equal("003D", store.SectionText);
        Assert.Equal(42L, store.SectionId);
        Assert.Equal(7L, store.EvaluationId);
        Assert.Equal(1, fake.LoadCount); // hidrata una sola vez, en construccion
    }

    [Fact]
    public void Constructor_EmptySnapshot_LeavesDefaults()
    {
        var fake = new FakePersistence(); // Stored = Empty

        var store = new SelectionStore(fake);

        Assert.Equal("", store.SectionText);
        Assert.Null(store.SectionId);
        Assert.Null(store.EvaluationId);
    }

    // ===== Setters actualizan las read props =====

    [Fact]
    public void SetSectionText_UpdatesReadProperty()
    {
        var store = new SelectionStore(new FakePersistence());

        store.SetSectionText("004D");

        Assert.Equal("004D", store.SectionText);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    [InlineData(null)]
    public void SetSectionId_UpdatesReadProperty(long? value)
    {
        var store = new SelectionStore(new FakePersistence());

        store.SetSectionId(value);

        Assert.Equal(value, store.SectionId);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(7L)]
    [InlineData(long.MaxValue)]
    [InlineData(null)]
    public void SetEvaluationId_UpdatesReadProperty(long? value)
    {
        var store = new SelectionStore(new FakePersistence());

        store.SetEvaluationId(value);

        Assert.Equal(value, store.EvaluationId);
    }

    // ===== Write-through: cada setter persiste el snapshot completo =====

    [Fact]
    public void SetSectionText_WritesThroughFullSnapshot()
    {
        var fake = new FakePersistence { Stored = new SelectionSnapshot("old", 1, 2) };
        var store = new SelectionStore(fake);

        store.SetSectionText("new");

        var saved = Assert.Single(fake.Saved);
        Assert.Equal(new SelectionSnapshot("new", 1, 2), saved);
    }

    [Fact]
    public void SetSectionId_WritesThroughFullSnapshot()
    {
        var fake = new FakePersistence { Stored = new SelectionSnapshot("003D", null, 5) };
        var store = new SelectionStore(fake);

        store.SetSectionId(99);

        var saved = Assert.Single(fake.Saved);
        Assert.Equal(new SelectionSnapshot("003D", 99, 5), saved);
    }

    [Fact]
    public void SetEvaluationId_WritesThroughFullSnapshot()
    {
        var fake = new FakePersistence { Stored = new SelectionSnapshot("003D", 8, 1) };
        var store = new SelectionStore(fake);

        store.SetEvaluationId(null);

        var saved = Assert.Single(fake.Saved);
        Assert.Equal(new SelectionSnapshot("003D", 8, null), saved);
    }

    // ===== Notificacion: cada setter dispara SelectionChanged exactamente una vez =====

    [Fact]
    public void SetSectionText_RaisesSelectionChangedExactlyOnce()
    {
        var store = new SelectionStore(new FakePersistence());
        var count = 0;
        store.SelectionChanged += (_, _) => count++;

        store.SetSectionText("x");

        Assert.Equal(1, count);
    }

    [Fact]
    public void SetSectionId_RaisesSelectionChangedExactlyOnce()
    {
        var store = new SelectionStore(new FakePersistence());
        var count = 0;
        store.SelectionChanged += (_, _) => count++;

        store.SetSectionId(3);

        Assert.Equal(1, count);
    }

    [Fact]
    public void SetEvaluationId_RaisesSelectionChangedExactlyOnce()
    {
        var store = new SelectionStore(new FakePersistence());
        var count = 0;
        store.SelectionChanged += (_, _) => count++;

        store.SetEvaluationId(3);

        Assert.Equal(1, count);
    }

    [Fact]
    public void SelectionChanged_PassesStoreAsSender()
    {
        var store = new SelectionStore(new FakePersistence());
        object? sender = null;
        store.SelectionChanged += (s, _) => sender = s;

        store.SetSectionText("x");

        Assert.Same(store, sender);
    }

    // ===== Clear: resetea, persiste y notifica =====

    [Fact]
    public void Clear_ResetsAllValuesToDefault()
    {
        var fake = new FakePersistence { Stored = new SelectionSnapshot("003D", 10, 20) };
        var store = new SelectionStore(fake);

        store.Clear();

        Assert.Equal("", store.SectionText);
        Assert.Null(store.SectionId);
        Assert.Null(store.EvaluationId);
    }

    [Fact]
    public void Clear_WritesThroughEmptySnapshot()
    {
        var fake = new FakePersistence { Stored = new SelectionSnapshot("003D", 10, 20) };
        var store = new SelectionStore(fake);

        store.Clear();

        var saved = Assert.Single(fake.Saved);
        Assert.Equal(SelectionSnapshot.Empty, saved);
    }

    [Fact]
    public void Clear_RaisesSelectionChangedExactlyOnce()
    {
        var store = new SelectionStore(new FakePersistence
        {
            Stored = new SelectionSnapshot("003D", 10, 20)
        });
        var count = 0;
        store.SelectionChanged += (_, _) => count++;

        store.Clear();

        Assert.Equal(1, count);
    }

    // ===== Contrato ante fallo de persistencia: surface, not swallow =====

    [Fact]
    public void SetSectionText_WhenPersistenceThrows_SurfacesException()
    {
        var fake = new FakePersistence { ThrowOnSave = true };
        var store = new SelectionStore(fake);

        // El store NO traga el fallo de persistencia: lo deja propagar (a
        // diferencia del antiguo StudentSection con catch {} vacio).
        Assert.Throws<InvalidOperationException>(() => store.SetSectionText("x"));
    }

    [Fact]
    public void SetSectionText_WhenPersistenceThrows_KeepsInMemoryValueAndDoesNotNotify()
    {
        var fake = new FakePersistence { ThrowOnSave = true };
        var store = new SelectionStore(fake);
        var notified = 0;
        store.SelectionChanged += (_, _) => notified++;

        Assert.Throws<InvalidOperationException>(() => store.SetSectionText("x"));

        // El estado en memoria es autoritativo y se actualiza ANTES del
        // write-through, asi que sobrevive aunque la persistencia falle.
        Assert.Equal("x", store.SectionText);
        // No se notifica un cambio que no se pudo persistir.
        Assert.Equal(0, notified);
    }
}
