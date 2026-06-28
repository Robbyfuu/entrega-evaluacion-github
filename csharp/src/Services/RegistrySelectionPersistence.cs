using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using EntregaEvaluacion.Core;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Adaptador de persistencia de la seleccion del alumno en HKCU. Lee y escribe
/// EXACTAMENTE las mismas claves que StudentSection (ruta Software\EntregaEvaluacion;
/// valores Section [TEXT], SectionId y EvaluationId [long como string]), de modo
/// que conviven: lo que escribe el store via este adaptador lo siguen leyendo los
/// consumidores que aun usan StudentSection (p. ej. SupabaseClient.IsForceLockdownAsync).
///
/// El happy path es byte-a-byte equivalente a StudentSection. La UNICA diferencia
/// es el manejo de errores: en vez del antiguo catch {} vacio que tragaba los
/// fallos en silencio, aca se capturan las excepciones especificas de registro,
/// se REGISTRAN via Debug.WriteLine (mismo patron tolerante a fallos que
/// ExamSessionService ante JSON corrupto) y se degrada con gracia (Load devuelve
/// Empty; Save no propaga), tal como StudentSection devolvia "" / null.
/// </summary>
public sealed class RegistrySelectionPersistence : ISelectionPersistence
{
    private const string RegPath = @"Software\EntregaEvaluacion";
    private const string RegNameSection = "Section";
    private const string RegNameSectionId = "SectionId";
    private const string RegNameEvaluationId = "EvaluationId";

    public SelectionSnapshot Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            if (key is null) return SelectionSnapshot.Empty;

            var sectionText = key.GetValue(RegNameSection) as string ?? "";
            var sectionId = ParseLong(key.GetValue(RegNameSectionId) as string);
            var evaluationId = ParseLong(key.GetValue(RegNameEvaluationId) as string);
            return new SelectionSnapshot(sectionText, sectionId, evaluationId);
        }
        catch (Exception ex)
            when (ex is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Debug.WriteLine($"[RegistrySelectionPersistence] No se pudo leer {RegPath}: {ex.Message}");
            return SelectionSnapshot.Empty;
        }
    }

    public void Save(SelectionSnapshot snapshot)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);

            // Section TEXT siempre se escribe (espeja StudentSection.Set).
            key.SetValue(RegNameSection, snapshot.SectionText);
            // long? -> string si tiene valor; DeleteValue si es null (igual que
            // StudentSection.SetSectionId / SetEvaluationId).
            WriteLong(key, RegNameSectionId, snapshot.SectionId);
            WriteLong(key, RegNameEvaluationId, snapshot.EvaluationId);
        }
        catch (Exception ex)
            when (ex is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Debug.WriteLine($"[RegistrySelectionPersistence] No se pudo persistir {RegPath}: {ex.Message}");
        }
    }

    private static long? ParseLong(string? raw)
        => long.TryParse(raw, out var id) ? id : null;

    private static void WriteLong(RegistryKey key, string name, long? value)
    {
        if (value is { } id)
            key.SetValue(name, id.ToString());
        else
            key.DeleteValue(name, throwOnMissingValue: false);
    }
}
