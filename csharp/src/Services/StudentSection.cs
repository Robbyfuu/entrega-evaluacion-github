using Microsoft.Win32;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Persiste la seccion y evaluacion seleccionada del alumno en HKCU.
/// Section (TEXT) se mantiene para coexistencia con clientes viejos;
/// SectionId + EvaluationId se agregan para multi-evaluacion.
/// </summary>
public static class StudentSection
{
    private const string RegPath = @"Software\EntregaEvaluacion";
    private const string RegName = "Section";
    private const string RegNameSectionId = "SectionId";
    private const string RegNameEvaluationId = "EvaluationId";

    public static string Get()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            return key?.GetValue(RegName) as string ?? "";
        }
        catch { return ""; }
    }

    public static void Set(string section)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);
            key?.SetValue(RegName, section);
        }
        catch { }
    }

    public static long? GetSectionId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            var v = key?.GetValue(RegNameSectionId) as string;
            return long.TryParse(v, out var id) ? id : null;
        }
        catch { return null; }
    }

    public static void SetSectionId(long? sectionId)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);
            if (sectionId is { } id)
                key?.SetValue(RegNameSectionId, id.ToString());
            else
                key?.DeleteValue(RegNameSectionId, false);
        }
        catch { }
    }

    public static long? GetEvaluationId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            var v = key?.GetValue(RegNameEvaluationId) as string;
            return long.TryParse(v, out var id) ? id : null;
        }
        catch { return null; }
    }

    public static void SetEvaluationId(long? evaluationId)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);
            if (evaluationId is { } id)
                key?.SetValue(RegNameEvaluationId, id.ToString());
            else
                key?.DeleteValue(RegNameEvaluationId, false);
        }
        catch { }
    }
}
