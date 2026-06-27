using System.Text.RegularExpressions;

namespace EntregaEvaluacion.Core;

/// <summary>
/// Normalizacion de nombres de repositorio: quita tildes, pasa a minusculas y
/// reduce a un slug compatible con GitHub ([a-z0-9-]). Logica pura, sin UI.
/// </summary>
public static partial class RepoNameSanitizer
{
    public static string Sanitize(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        var clean = sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
        clean = WhitespaceRegex().Replace(clean, "-");
        clean = InvalidCharsRegex().Replace(clean, "");
        clean = HyphenRunRegex().Replace(clean, "-").Trim('-');
        return clean;
    }

    // Source-generated regex (.NET 8): se compilan en build, sin re-parsear en
    // cada llamada (Sanitize corre en bucles por tarea). Mismos patrones.
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex InvalidCharsRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex HyphenRunRegex();
}
