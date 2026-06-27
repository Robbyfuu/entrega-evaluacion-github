namespace EntregaEvaluacion.Core;

/// <summary>
/// Logica pura de procesos sospechosos: normalizacion + lista fallback. Vive en
/// Core (net8.0, cross-platform) para ser testeable en mac/CI; la app WPF la
/// consume via los re-exports delgados de <c>Config</c>.
///
/// DEBE mantenerse en paridad byte-a-byte con
/// <c>admin-next/lib/suspicious.ts</c> (normalizeProcessName +
/// FALLBACK_SUSPICIOUS_PROCESSES) y con el seed de la migracion SQL. Los golden
/// tests (SuspiciousProcessesTests.cs y suspicious.test.ts) congelan esa
/// paridad: si divergen, una de las dos implementaciones rompio el contrato.
/// </summary>
public static class SuspiciousProcesses
{
    /// <summary>
    /// Normaliza un nombre de proceso para comparar contra el blocklist.
    ///
    /// Algoritmo (en orden, exacto):
    ///   1. null/vacio/whitespace -> "".
    ///   2. trim (recorta espacios al inicio/fin).
    ///   3. lowercase invariante (ToLowerInvariant).
    ///   4. si termina en ".exe" (case-insensitive, ya cubierto por el lowercase
    ///      previo), quitar ese sufijo de 4 chars.
    ///   5. trim final (por si quedaba espacio antes del ".exe").
    /// Ej.: "Chrome.exe" -> "chrome"; "  CODE  " -> "code"; "notepad++" -> "notepad++".
    /// </summary>
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = name.Trim().ToLowerInvariant();
        if (s.EndsWith(".exe", StringComparison.Ordinal))
            s = s.Substring(0, s.Length - 4);
        return s.Trim();
    }

    /// <summary>
    /// Procesos sospechosos FALLBACK. La fuente de verdad en produccion es la
    /// tabla <c>suspicious_processes</c>; esta lista solo se usa cuando el fetch
    /// falla por red o devuelve vacio. Mantener normalizada (sin .exe, lowercase)
    /// y sincronizada con el seed de la migracion y con
    /// FALLBACK_SUSPICIOUS_PROCESSES (TS).
    /// </summary>
    public static readonly string[] Fallback =
    {
        "chrome", "msedge", "firefox", "opera", "brave", "iexplore", "vivaldi", "tor",
        "whatsapp", "discord", "telegram", "slack", "teams", "skype",
        "notion", "obsidian", "evernote", "onenote", "winword", "excel",
        "code", "pycharm", "pycharm64", "sublime_text", "notepad", "notepad++", "devenv",
        "anydesk", "teamviewer", "rustdesk", "msrdc",
        "chatgpt", "claude", "copilot"
    };
}
