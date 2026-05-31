using System.Windows;
using System.Windows.Input;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Windows;

/// <summary>
/// Dialogo para ingresar la clave del profesor. Valida contra LockdownService
/// y ejecuta Release() si es correcta. Devuelve DialogResult=true al liberar.
/// </summary>
public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PwdBox.Focus();
    }

    private void TryValidate()
    {
        if (LockdownService.VerifyPassword(PwdBox.Password))
        {
            LockdownService.Release();
            DialogResult = true;
            Close();
        }
        else
        {
            ErrText.Text = "Clave incorrecta.";
            PwdBox.Clear();
            PwdBox.Focus();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => TryValidate();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PwdBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryValidate();
    }
}
