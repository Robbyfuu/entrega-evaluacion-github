using System.Drawing;
using System.Windows.Forms;
using EntregaEvaluacion.Services;

namespace EntregaEvaluacion.Forms;

/// <summary>
/// Dialog de login via device flow. Muestra codigo grande + URL. Hace polling
/// del token y cierra con OK cuando el alumno autoriza.
/// </summary>
public class LoginDialog : Form
{
    private readonly GitHubService _gh;
    private System.Windows.Forms.Timer? _pollTimer;
    private string _deviceCode = "";
    private DateTime _expiresAt;
    private string _verifyUri = "";

    private readonly Label _lblStatus;
    private readonly Label _lblTime;
    private readonly ProgressBar _progress;

    public LoginDialog(GitHubService gh)
    {
        _gh = gh;

        Text = "Iniciar sesion en GitHub";
        Size = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.FontBody;

        var lblP1 = new Label
        {
            Text = "PASO 1: Abre esta URL en tu navegador o celular",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(20, 20),
            Size = new Size(500, 22)
        };
        Controls.Add(lblP1);

        var txtUrl = new TextBox
        {
            ReadOnly = true,
            Font = new Font("Consolas", 11),
            Location = new Point(20, 48),
            Size = new Size(380, 25),
            Text = "https://github.com/login/device"
        };
        Controls.Add(txtUrl);

        var btnCopyUrl = new Button
        {
            Text = "Copiar",
            Location = new Point(410, 47),
            Size = new Size(110, 27)
        };
        btnCopyUrl.Click += (_, _) => { try { Clipboard.SetText(_verifyUri); btnCopyUrl.Text = "Copiado!"; } catch { } };
        Controls.Add(btnCopyUrl);

        var lblP2 = new Label
        {
            Text = "PASO 2: Ingresa este codigo",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(20, 95),
            Size = new Size(500, 22)
        };
        Controls.Add(lblP2);

        var lblCode = new Label
        {
            Name = "lblCode",
            Text = "....-....",
            Font = new Font("Consolas", 28, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(33, 33, 33),
            ForeColor = Color.LimeGreen,
            Location = new Point(20, 125),
            Size = new Size(380, 60)
        };
        Controls.Add(lblCode);

        var btnCopyCode = new Button
        {
            Text = "Copiar codigo",
            Location = new Point(410, 140),
            Size = new Size(110, 32)
        };
        btnCopyCode.Click += (_, _) => { try { Clipboard.SetText(lblCode.Text); btnCopyCode.Text = "Copiado!"; } catch { } };
        Controls.Add(btnCopyCode);

        var btnOpen = new Button
        {
            Text = "Abrir URL en navegador (opcional)",
            Location = new Point(20, 205),
            Size = new Size(500, 32)
        };
        btnOpen.Click += (_, _) => OpenUrl(_verifyUri);
        Controls.Add(btnOpen);

        _lblStatus = new Label
        {
            Text = "Solicitando codigo...",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.DarkOrange,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 255),
            Size = new Size(500, 22)
        };
        Controls.Add(_lblStatus);

        _progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Location = new Point(20, 285),
            Size = new Size(500, 12)
        };
        Controls.Add(_progress);

        _lblTime = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 310),
            Size = new Size(500, 18)
        };
        Controls.Add(_lblTime);

        var btnCancel = new Button
        {
            Text = "Cancelar",
            Location = new Point(210, 395),
            Size = new Size(120, 32),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(btnCancel);
        CancelButton = btnCancel;

        Shown += async (_, _) => await StartFlowAsync(lblCode);
    }

    private async Task StartFlowAsync(Label lblCode)
    {
        var dc = await _gh.RequestDeviceCodeAsync();
        if (dc == null)
        {
            _lblStatus.Text = "Error contactando GitHub. Revisa tu internet.";
            _lblStatus.ForeColor = Color.Red;
            _progress.Style = ProgressBarStyle.Continuous;
            return;
        }

        _deviceCode = dc.DeviceCode;
        _verifyUri = dc.VerificationUri;
        _expiresAt = DateTime.UtcNow.AddSeconds(dc.ExpiresIn);
        lblCode.Text = dc.UserCode;
        _lblStatus.Text = "Esperando que ingreses el codigo...";

        _pollTimer = new System.Windows.Forms.Timer { Interval = Math.Max(dc.Interval, 5) * 1000 };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _pollTimer.Start();
    }

    private async Task PollAsync()
    {
        var remaining = (int)(_expiresAt - DateTime.UtcNow).TotalSeconds;
        if (remaining <= 0)
        {
            _pollTimer?.Stop();
            _lblStatus.Text = "Codigo expirado. Cierra y vuelve a intentar.";
            _lblStatus.ForeColor = Color.Red;
            _progress.Style = ProgressBarStyle.Continuous;
            return;
        }
        _lblTime.Text = $"Tiempo restante: {remaining / 60} min {remaining % 60} seg";

        try
        {
            var token = await _gh.PollAccessTokenAsync(_deviceCode);
            if (!string.IsNullOrEmpty(token))
            {
                _pollTimer?.Stop();
                _lblStatus.Text = "Autorizado! Sesion iniciada.";
                _lblStatus.ForeColor = Color.Green;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 100;
                DialogResult = DialogResult.OK;
                await Task.Delay(600);
                Close();
            }
        }
        catch (TimeoutException)
        {
            _pollTimer?.Stop();
            _lblStatus.Text = "Codigo expirado.";
            _lblStatus.ForeColor = Color.Red;
        }
        catch (UnauthorizedAccessException)
        {
            _pollTimer?.Stop();
            _lblStatus.Text = "Acceso denegado.";
            _lblStatus.ForeColor = Color.Red;
        }
        catch { /* reintentar */ }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
