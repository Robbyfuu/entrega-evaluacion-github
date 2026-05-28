using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Operaciones git via LibGit2Sharp (nativo, NO requiere git instalado).
/// Clone, commit, push con auth por token. Anti-trampa de repo limpio.
/// </summary>
public class GitService
{
    private readonly string _token;
    private readonly string _userName;
    private readonly string _userEmail;

    public GitService(string token, string userName, string userEmail)
    {
        _token = token;
        _userName = userName;
        _userEmail = string.IsNullOrEmpty(userEmail)
            ? $"{userName}@users.noreply.github.com" : userEmail;
    }

    private CredentialsHandler Creds => (_url, _user, _types) =>
        new UsernamePasswordCredentials { Username = _token, Password = "" };

    public class CloneResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Path { get; set; }
        public bool Reused { get; set; }
    }

    /// <summary>
    /// Clona repoOwner/repoName a destFolder. Si ya existe, lo reutiliza.
    /// </summary>
    public CloneResult Clone(string repoOwner, string repoName, string destFolder)
    {
        var url = $"https://github.com/{repoOwner}/{repoName}.git";
        try
        {
            if (Directory.Exists(destFolder) && Directory.Exists(Path.Combine(destFolder, ".git")))
            {
                return new CloneResult { Ok = true, Path = destFolder, Reused = true };
            }

            var opts = new CloneOptions();
            opts.FetchOptions.CredentialsProvider = Creds;
            Repository.Clone(url, destFolder, opts);
            return new CloneResult { Ok = true, Path = destFolder, Reused = false };
        }
        catch (Exception ex)
        {
            return new CloneResult { Ok = false, Error = ex.Message };
        }
    }

    public class CleanCheck
    {
        public bool IsClean { get; set; }
        public int FilesCount { get; set; }
        public string[] FilesNames { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Verifica que el repo no tenga archivos pre-existentes (anti-trampa).
    /// Permite solo README, LICENSE, .gitignore, .gitattributes, .git.
    /// </summary>
    public static CleanCheck TestRepoIsClean(string folder)
    {
        var allowed = new HashSet<string>(Config.AllowedRepoFiles, StringComparer.OrdinalIgnoreCase);
        var suspicious = new List<string>();
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(folder))
            {
                var name = Path.GetFileName(entry);
                if (!allowed.Contains(name)) suspicious.Add(name);
            }
        }
        catch { }

        return new CleanCheck
        {
            IsClean = suspicious.Count == 0,
            FilesCount = suspicious.Count,
            FilesNames = suspicious.Take(10).ToArray()
        };
    }

    public class PushResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Url { get; set; }
    }

    /// <summary>
    /// Stage all + commit + push. Si el folder no es repo, lo inicializa
    /// apuntando a repoOwner/repoName.
    /// </summary>
    public PushResult CommitAndPush(
        string folder, string repoOwner, string repoName, string commitMessage)
    {
        var url = $"https://github.com/{repoOwner}/{repoName}.git";
        try
        {
            // Inicializar si no es repo
            if (!Directory.Exists(Path.Combine(folder, ".git")))
            {
                Repository.Init(folder);
            }

            using var repo = new Repository(folder);

            // Configurar remote origin
            var origin = repo.Network.Remotes["origin"];
            if (origin == null)
                repo.Network.Remotes.Add("origin", url);
            else if (origin.Url != url)
                repo.Network.Remotes.Update("origin", r => r.Url = url);

            // Stage all
            Commands.Stage(repo, "*");

            // Commit (si hay cambios)
            var sig = new Signature(_userName, _userEmail, DateTimeOffset.Now);
            var status = repo.RetrieveStatus();
            if (status.IsDirty)
            {
                repo.Commit(commitMessage, sig, sig);
            }

            // Detectar branch actual
            var branch = repo.Head.FriendlyName;
            if (string.IsNullOrEmpty(branch) || branch == "(no branch)")
                branch = "main";

            // Push
            var pushOpts = new PushOptions { CredentialsProvider = Creds };
            var refSpec = $"refs/heads/{branch}:refs/heads/{branch}";

            try
            {
                repo.Network.Push(repo.Network.Remotes["origin"], refSpec, pushOpts);
            }
            catch (NonFastForwardException)
            {
                // Divergencia (repo Classroom con README inicial): pull --rebase + retry
                PullRebase(repo, branch);
                repo.Network.Push(repo.Network.Remotes["origin"], refSpec, pushOpts);
            }

            return new PushResult
            {
                Ok = true,
                Url = $"https://github.com/{repoOwner}/{repoName}"
            };
        }
        catch (Exception ex)
        {
            return new PushResult { Ok = false, Error = ex.Message };
        }
    }

    private void PullRebase(Repository repo, string branch)
    {
        var fetchOpts = new FetchOptions { CredentialsProvider = Creds };
        var remote = repo.Network.Remotes["origin"];
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
        Commands.Fetch(repo, remote.Name, refSpecs, fetchOpts, null);

        var sig = new Signature(_userName, _userEmail, DateTimeOffset.Now);
        var opts = new PullOptions
        {
            FetchOptions = fetchOpts,
            MergeOptions = new MergeOptions { FastForwardStrategy = FastForwardStrategy.Default }
        };
        try { Commands.Pull(repo, sig, opts); } catch { }
    }

    /// <summary>
    /// Inspecciona el remote origin de una carpeta y devuelve el owner del repo.
    /// </summary>
    public static (string? owner, string? url) GetRemoteOwner(string folder)
    {
        try
        {
            if (!Directory.Exists(Path.Combine(folder, ".git"))) return (null, null);
            using var repo = new Repository(folder);
            var origin = repo.Network.Remotes["origin"];
            if (origin == null) return (null, null);
            var url = origin.Url;
            // Parsear owner de https://github.com/OWNER/REPO.git
            var m = System.Text.RegularExpressions.Regex.Match(
                url, @"github\.com[:/](?:[^/]+@)?([^/]+)/[^/]+?(?:\.git)?/?$");
            return (m.Success ? m.Groups[1].Value : null, url);
        }
        catch { return (null, null); }
    }
}
