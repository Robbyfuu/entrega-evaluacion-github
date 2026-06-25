using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Tema claro/oscuro. Aplica el tema de WPF-UI (controles Fluent) y mergea/
/// desmergea el override oscuro de los brushes propios (ConsolaOps.Dark.xaml).
/// Los estilos referencian los brushes con DynamicResource, asi que el swap es
/// en vivo. Persiste la eleccion en el registro (HKCU). Default = claro.
/// </summary>
public static class ThemeService
{
    private const string RegPath = @"Software\EntregaEvaluacion";
    private const string RegName = "DarkMode";

    private static ResourceDictionary? _darkDict;

    public static bool IsDark { get; private set; }

    /// <summary>Aplica el tema guardado (llamar al arrancar).</summary>
    public static void ApplySaved() => Apply(LoadSaved());

    public static void Toggle() => Apply(!IsDark);

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var app = Application.Current;
        if (app == null) return;

        try { ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light); }
        catch (Exception ex) { Debug.WriteLine($"[Theme] WPF-UI Apply fallo: {ex.Message}"); }

        try
        {
            if (dark)
            {
                _darkDict ??= new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Resources/ConsolaOps.Dark.xaml")
                };
                if (!app.Resources.MergedDictionaries.Contains(_darkDict))
                    app.Resources.MergedDictionaries.Add(_darkDict);
            }
            else if (_darkDict != null)
            {
                app.Resources.MergedDictionaries.Remove(_darkDict);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Theme] swap brushes fallo: {ex.Message}"); }

        Save(dark);
    }

    private static bool LoadSaved()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            return (key?.GetValue(RegName) as string) == "1";
        }
        catch { return false; }
    }

    private static void Save(bool dark)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);
            key?.SetValue(RegName, dark ? "1" : "0");
        }
        catch (Exception ex) { Debug.WriteLine($"[Theme] Save fallo: {ex.Message}"); }
    }
}
