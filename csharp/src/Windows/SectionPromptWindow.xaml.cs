using System.Windows;
using EntregaEvaluacion;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Dialogo modal para elegir la seccion del alumno cuando no hay una guardada.
/// Reemplaza al Form inline de MainForm.PromptSection conservando su logica.
/// </summary>
public partial class SectionPromptWindow : Window
{
    public string SelectedSection { get; private set; } = Config.Sections[0];

    public SectionPromptWindow()
    {
        InitializeComponent();
        foreach (var s in Config.Sections) SectionCombo.Items.Add(s);
        SectionCombo.SelectedIndex = 0;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        SelectedSection = (string)SectionCombo.SelectedItem!;
        Close();
    }
}
