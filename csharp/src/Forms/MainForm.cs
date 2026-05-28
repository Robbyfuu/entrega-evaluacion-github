using System.Drawing;
using System.Windows.Forms;

namespace EntregaEvaluacion.Forms;

/// <summary>
/// Formulario principal. STUB inicial: se completa en Task #8.
/// </summary>
public class MainForm : Form
{
    public MainForm()
    {
        Text = "Subir Evaluacion a GitHub";
        Size = new Size(640, 705);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var lbl = new Label
        {
            Text = "Evaluacion -> GitHub (C# build OK)",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Location = new Point(20, 20),
            Size = new Size(580, 40)
        };
        Controls.Add(lbl);
    }
}
