using System.Text.RegularExpressions;

namespace EntregaEvaluacion.Core;

/// <summary>
/// Normalizacion de nombres de repositorio: quita tildes, pasa a minusculas y
/// reduce a un slug compatible con GitHub ([a-z0-9-]). Logica pura, sin UI.
/// </summary>
public static class RepoNameSanitizer
{
    public static string Sanitize(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        var clean = sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
        clean = Regex.Replace(clean, @"\s+", "-");
        clean = Regex.Replace(clean, @"[^a-z0-9\-]", "");
        clean = Regex.Replace(clean, @"-+", "-").Trim('-');
        return clean;
    }
}
