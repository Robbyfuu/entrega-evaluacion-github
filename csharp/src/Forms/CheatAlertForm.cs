using System.Drawing;
using System.Windows.Forms;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Forms;

/// <summary>
/// Pantalla roja de bloqueo (kiosk). Sin boton X, TopMost. Se libera con
/// clave del profesor o (si es remoto) cuando el profe libera desde el panel.
/// </summary>
public class CheatAlertForm : Form
{
    private readonly bool _remoteSource;
    private readonly Func<bool>? _checkStillLocked;
    private System.Windows.Forms.Timer? _releaseTimer;

    public CheatAlertForm(
        string repoName,
        int filesCount,
        string[] filesSample,
        bool isPersistent = false,
        bool remoteSource = false,
        Func<bool>? checkStillLocked = null)
    {
        _remoteSource = remoteSource;
        _checkStillLocked = checkStillLocked;

        Text = "ALERTA DE INTEGRIDAD ACADEMICA";
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        BackColor = Color.FromArgb(183, 28, 28);
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;

        var lblWarn = new Label
        {
            Text = "! ALERTA !",
            Font = new Font("Segoe UI", 42, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 20),
            Size = new Size(720, 70)
        };
        Controls.Add(lblWarn);

        var lblTitle = new Label
        {
            Text = "POSIBLE TRAMPA ACADEMICA DETECTADA",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 95),
            Size = new Size(720, 30)
        };
        Controls.Add(lblTitle);

        var sample = string.Join(", ", filesSample);
        var msg =
            $"Repositorio: '{repoName}' contiene {filesCount} archivo(s) NO permitidos:\n\n" +
            $"  {sample}\n\n" +
            "Una evaluacion en blanco solo deberia tener README, LICENSE o .gitignore.\n\n" +
            "Este intento fue REGISTRADO. El profesor sera notificado.\n\n" +
            "Esta ventana esta BLOQUEADA y el Administrador de Tareas tambien.\n" +
            "Solo el profesor puede desbloquear este equipo con su clave.";
        if (isPersistent)
            msg += "\n\n[Detectado en sesion anterior. El bloqueo persistira en cada reinicio.]";

        var lblMsg = new Label
        {
            Text = msg,
            Font = new Font("Segoe UI", 11),
            ForeColor = Color.White,
            Location = new Point(40, 140),
            Size = new Size(680, 300)
        };
        Controls.Add(lblMsg);

        var btnUnlock = new Button
        {
            Text = "Desbloquear (clave del profesor)",
            Location = new Point(260, 460),
            Size = new Size(280, 50),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(183, 28, 28),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        btnUnlock.Click += (_, _) => PromptPassword();
        Controls.Add(btnUnlock);

        // Bloquear cierre
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) e.Cancel = true;
        };

        // Bloquear teclas problematicas
        KeyDown += (_, e) =>
        {
            if ((e.Alt && e.KeyCode == Keys.F4) ||
                (e.Alt && e.KeyCode == Keys.Tab) ||
                e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        };

        // Timer: forzar al frente + release remoto
        Shown += (_, _) =>
        {
            TopMost = true;
            Activate();
            BringToFront();

            if (_remoteSource && _checkStillLocked != null)
            {
                _releaseTimer = new System.Windows.Forms.Timer { Interval = 10000 };
                _releaseTimer.Tick += (_, _) =>
                {
                    try
                    {
                        if (!_checkStillLocked())
                        {
                            _releaseTimer?.Stop();
                            LockdownService.Release();
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    }
                    catch { }
                };
                _releaseTimer.Start();
            }
        };
    }

    private void PromptPassword()
    {
        using var dlg = new Form
        {
            Text = "Clave del profesor",
            Size = new Size(380, 200),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ControlBox = false,
            TopMost = true
        };
        var lbl = new Label
        {
            Text = "Ingrese la clave de desbloqueo:",
            Location = new Point(20, 20),
            Size = new Size(330, 22),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        var txt = new TextBox
        {
            Location = new Point(20, 50),
            Size = new Size(330, 28),
            PasswordChar = '*',
            Font = new Font("Consolas", 12)
        };
        var lblErr = new Label
        {
            ForeColor = Color.Red,
            Location = new Point(20, 85),
            Size = new Size(330, 20),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        var btnOk = new Button
        {
            Text = "Validar",
            Location = new Point(180, 120),
            Size = new Size(80, 32),
            BackColor = Color.FromArgb(76, 175, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnOk.Click += (_, _) =>
        {
            if (LockdownService.VerifyPassword(txt.Text))
            {
                LockdownService.Release();
                DialogResult = DialogResult.OK;
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
                Close();
            }
            else
            {
                lblErr.Text = "Clave incorrecta.";
                txt.Clear();
                txt.Focus();
            }
        };
        var btnCancel = new Button
        {
            Text = "Cancelar",
            Location = new Point(270, 120),
            Size = new Size(80, 32),
            DialogResult = DialogResult.Cancel
        };
        dlg.Controls.AddRange(new Control[] { lbl, txt, lblErr, btnOk, btnCancel });
        dlg.AcceptButton = btnOk;
        dlg.CancelButton = btnCancel;
        dlg.ShowDialog(this);
    }
}
