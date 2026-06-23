namespace EntregaEvaluacion;

/// <summary>
/// Constantes globales de la aplicacion. La anon key de Supabase es
/// safe-to-share por diseno (RLS protege escrituras).
/// </summary>
public static class Config
{
    public const string SupabaseUrl = "https://oiownlxyquarmqwauegf.supabase.co";

    public const string SupabaseAnonKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im9pb3dubHh5cXVhcm1xd2F1ZWdmIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzkyMDk5NTEsImV4cCI6MjA5NDc4NTk1MX0.MMODHCBz_xl3gnzJVfY-aIQPyINQDkwXyr-e6KPtrm4";

    // OAuth Client ID publico del GitHub CLI oficial (device flow)
    public const string GitHubClientId = "178c6fc778ccc68e1d6a";

    // Scopes que pedimos al device flow
    public const string GitHubScopes = "repo workflow read:org gist";

    // Intervalo de polling (heartbeat + admin config) en ms
    public const int PollIntervalMs = 20000;

    // Secciones disponibles. FALLBACK: la fuente de verdad en produccion es la
    // tabla `sections` (fetcheada por SupabaseClient.GetSectionsAsync al
    // arrancar). Esta lista solo se usa cuando el fetch falla por red o
    // devuelve vacio (mismo patron que SuspiciousProcesses). Mantener
    // sincronizada con el seed de migration-multi-evaluation.sql.
    public static readonly string[] Sections = { "001D", "002D", "003D" };

    // Tipos de evaluacion. FALLBACK: la fuente de verdad en produccion es la
    // tabla `evaluations` (fetcheada por SupabaseClient.GetEvaluationsAsync).
    // Esta lista solo se usa cuando el fetch falla. Mantener sincronizada con
    // el seed de migration-multi-evaluation.sql.
    public static readonly string[] EvaluationTypes =
        { "Evaluacion-1", "Evaluacion-2", "Evaluacion-3", "Evaluacion-4", "Examen" };

    // Procesos sospechosos. FALLBACK: la fuente de verdad en produccion es la
    // tabla `suspicious_processes` (global section=NULL union extras por seccion),
    // cacheada por el cliente en cada AdminTick. Esta lista solo se usa cuando el
    // fetch a la tabla falla por red O devuelve vacio (ver ProcessMonitor.IsSuspicious
    // y SupabaseClient.GetBlocklistAsync). Mantener normalizada (sin .exe, lowercase)
    // y sincronizada con el seed de la migracion.
    public static readonly string[] SuspiciousProcesses =
    {
        "chrome", "msedge", "firefox", "opera", "brave", "iexplore", "vivaldi", "tor",
        "whatsapp", "discord", "telegram", "slack", "teams", "skype",
        "notion", "obsidian", "evernote", "onenote", "winword", "excel",
        "code", "pycharm", "pycharm64", "sublime_text", "notepad", "notepad++", "devenv",
        "anydesk", "teamviewer", "rustdesk", "msrdc",
        "chatgpt", "claude", "copilot"
    };

    /// <summary>
    /// Normaliza un nombre de proceso para comparar contra el blocklist. DEBE
    /// ser identico al seed de la migracion SQL y al normalizeProcessName del
    /// panel (TS) para garantizar paridad de deteccion.
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
    public static string NormalizeProcessName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = name.Trim().ToLowerInvariant();
        if (s.EndsWith(".exe", StringComparison.Ordinal))
            s = s.Substring(0, s.Length - 4);
        return s.Trim();
    }

    // Archivos permitidos en un repo "limpio" (anti-trampa)
    public static readonly string[] AllowedRepoFiles =
    {
        "README.md", "README", "README.txt", "README.rst",
        "LICENSE", "LICENSE.txt", "LICENSE.md",
        ".gitignore", ".gitattributes", ".git"
    };

    // ===== Whitelist de dominios para el navegador embebido =====
    // Solo se permite navegar a estos dominios (y sus subdominios). Cualquier
    // navegacion fuera de esta lista dispara la trampa (lockdown). El match es
    // por sufijo de host, case-insensitive (ver IsDomainAllowed).
    public static readonly string[] AllowedBrowseDomains =
    {
        // GitHub (cubre www, classroom, codeload, etc.). El alumno necesita
        // navegar github, asi que se permite todo el dominio.
        "github.com",
        "githubusercontent.com",

        // Entra ID / Microsoft login. Acotado a hosts de auth (no microsoft.com
        // entero, que abriria docs/office/etc).
        "login.microsoftonline.com",
        "login.live.com",
        "login.microsoft.com",
        "msftauth.net",
        "aadcdn.msftauth.net",

        // Google login + Gmail (aceptar invitacion). Acotado: NO google.com
        // entero (que permitiria Search/Translate/Docs y seria un escape).
        "accounts.google.com",
        "mail.google.com",
        "googleusercontent.com"
    };

    /// <summary>
    /// True si el host pertenece a un dominio permitido: coincide exactamente
    /// con un dominio de la whitelist o es un subdominio de el (termina en
    /// "." + dominio). Comparacion case-insensitive.
    /// </summary>
    public static bool IsDomainAllowed(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (var domain in AllowedBrowseDomains)
        {
            var d = domain.ToLowerInvariant();
            if (host == d || host.EndsWith("." + d, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ===== URLs exactas permitidas (por prefijo de ruta) =====
    // Para endpoints de SSO que viven en un host que NO queremos abrir entero.
    // Ej: el ACS (Assertion Consumer Service) SAML de DUOC vive en
    // www.google.com, pero abrir www.google.com completo seria un escape
    // (Search/Docs/Translate). Aqui se permite SOLO esta ruta puntual para que
    // funcione el inicio de sesion con Google/Microsoft, sin abrir el host.
    public static readonly string[] AllowedExactUrls =
    {
        "https://www.google.com/a/duocuc.cl/acs",
    };

    /// <summary>
    /// True si la URL puede navegarse: su host esta en la whitelist de dominios
    /// (IsDomainAllowed), O la URL coincide por prefijo (ignorando query y
    /// fragment) con una de AllowedExactUrls. Permite habilitar un endpoint
    /// puntual (p.ej. el ACS SSO de DUOC) sin abrir todo su host.
    /// </summary>
    public static bool IsUrlAllowed(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        Uri uri;
        try { uri = new Uri(url); } catch { return false; }

        if (IsDomainAllowed(uri.Host)) return true;

        // Comparar scheme://host/path (sin query ni fragment), case-insensitive.
        var normalized = (uri.Scheme + "://" + uri.Host + uri.AbsolutePath).ToLowerInvariant();
        foreach (var allowed in AllowedExactUrls)
        {
            if (normalized.StartsWith(allowed.ToLowerInvariant(), StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
