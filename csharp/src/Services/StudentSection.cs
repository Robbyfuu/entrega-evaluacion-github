using Microsoft.Win32;

namespace EntregaEvaluacion.Services;

/// <summary>Persiste la seccion del alumno en HKCU.</summary>
public static class StudentSection
{
    private const string RegPath = @"Software\EntregaEvaluacion";
    private const string RegName = "Section";

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
}
