using System.Drawing;
using System.Windows.Forms;

namespace EntregaEvaluacion;

/// <summary>
/// Design system "Consola Ops" para WinForms. Tokens de color, fuentes y
/// helpers para estilizar controles de forma consistente.
/// </summary>
public static class Theme
{
    // ===== Superficies =====
    public static readonly Color Bg          = ColorTranslator.FromHtml("#F7F8FA");
    public static readonly Color Surface     = ColorTranslator.FromHtml("#FFFFFF");
    public static readonly Color Surface2    = ColorTranslator.FromHtml("#F0F2F5");
    public static readonly Color Border      = ColorTranslator.FromHtml("#E2E5EA");
    public static readonly Color BorderStrong= ColorTranslator.FromHtml("#CBD0D8");

    // ===== Texto =====
    public static readonly Color Text        = ColorTranslator.FromHtml("#1A1D23");
    public static readonly Color TextMuted    = ColorTranslator.FromHtml("#6B7280");
    public static readonly Color TextFaint   = ColorTranslator.FromHtml("#9CA3AF");

    // ===== Semantico =====
    public static readonly Color Primary     = ColorTranslator.FromHtml("#2563EB");
    public static readonly Color Danger      = ColorTranslator.FromHtml("#DC2626");
    public static readonly Color Success     = ColorTranslator.FromHtml("#16A34A");
    public static readonly Color Warning     = ColorTranslator.FromHtml("#D97706");
    public static readonly Color Info        = ColorTranslator.FromHtml("#7C3AED");
    public static readonly Color White       = Color.White;

    // Consola (log)
    public static readonly Color ConsoleBg   = ColorTranslator.FromHtml("#0D0E14");
    public static readonly Color ConsoleFg   = ColorTranslator.FromHtml("#4ADE80");

    // ===== Fuentes (nativas Windows, feel Consola Ops) =====
    public static Font FontTitle  => new("Segoe UI Semibold", 16f, FontStyle.Bold);
    public static Font FontH2     => new("Segoe UI Semibold", 11f, FontStyle.Bold);
    public static Font FontBody   => new("Segoe UI", 9.5f, FontStyle.Regular);
    public static Font FontLabel  => new("Segoe UI Semibold", 8.5f, FontStyle.Bold);
    public static Font FontMono   => new("Consolas", 9.5f, FontStyle.Regular);
    public static Font FontMonoBig=> new("Consolas", 22f, FontStyle.Bold);

    // ===== Helpers =====
    public static void StylePrimary(Button b)   => StyleButton(b, Primary, White);
    public static void StyleDanger(Button b)    => StyleButton(b, Danger, White);
    public static void StyleSuccess(Button b)   => StyleButton(b, Success, White);
    public static void StyleSecondary(Button b) => StyleOutline(b);

    public static void StyleButton(Button b, Color bg, Color fg)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = bg;
        b.ForeColor = fg;
        b.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.1f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.05f);
    }

    public static void StyleOutline(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = BorderStrong;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = Surface;
        b.ForeColor = Text;
        b.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        b.Cursor = Cursors.Hand;
        b.FlatAppearance.MouseOverBackColor = Surface2;
    }

    public static void StyleInput(TextBox t)
    {
        t.BorderStyle = BorderStyle.FixedSingle;
        t.BackColor = Surface;
        t.ForeColor = Text;
        t.Font = FontBody;
    }

    public static void StyleCombo(ComboBox c)
    {
        c.FlatStyle = FlatStyle.Flat;
        c.BackColor = Surface;
        c.ForeColor = Text;
        c.Font = FontBody;
    }

    /// <summary>Crea un GroupBox tipo "card" con titulo.</summary>
    public static GroupBox Card(string title, Point loc, Size size)
    {
        return new GroupBox
        {
            Text = title,
            Location = loc,
            Size = size,
            Font = FontLabel,
            ForeColor = TextMuted,
            BackColor = Surface
        };
    }
}
