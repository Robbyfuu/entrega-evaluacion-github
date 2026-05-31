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

    // Secciones disponibles
    public static readonly string[] Sections = { "001D", "002D", "003D" };

    // Tipos de evaluacion
    public static readonly string[] EvaluationTypes =
        { "Evaluacion-1", "Evaluacion-2", "Evaluacion-3", "Evaluacion-4", "Examen" };

    // Procesos sospechosos (sin powershell/cmd: el propio exe no, pero por las dudas)
    public static readonly string[] SuspiciousProcesses =
    {
        "chrome", "msedge", "firefox", "opera", "brave", "iexplore", "vivaldi", "tor",
        "whatsapp", "discord", "telegram", "slack", "teams", "skype",
        "notion", "obsidian", "evernote", "onenote", "winword", "excel",
        "code", "pycharm", "pycharm64", "sublime_text", "notepad", "notepad++", "devenv",
        "anydesk", "teamviewer", "rustdesk", "msrdc",
        "chatgpt", "claude", "copilot"
    };

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
}
