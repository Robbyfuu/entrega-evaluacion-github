using System.Text.Json.Serialization;

namespace EntregaEvaluacion.Models;

public class ControlState
{
    [JsonPropertyName("internet_block")] public bool InternetBlock { get; set; }
    [JsonPropertyName("force_lockdown")] public bool ForceLockdown { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("updated_by")] public string? UpdatedBy { get; set; }
}

public class Assignment
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("classroom_url")] public string ClassroomUrl { get; set; } = "";
    [JsonPropertyName("section")] public string? Section { get; set; }
    [JsonPropertyName("org")] public string? Org { get; set; }
    [JsonPropertyName("active")] public bool Active { get; set; }
}

public class TargetedLockdown
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("pc_name")] public string PcName { get; set; } = "";
    [JsonPropertyName("github_username")] public string GithubUsername { get; set; } = "";
    [JsonPropertyName("active")] public bool Active { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

public class ProcessInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("pid")] public int Pid { get; set; }
}

// DTOs para repos de GitHub (deserializar /user/repos y /repos/{owner}/{repo})
public class GitHubRepo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
    [JsonPropertyName("private")] public bool Private { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("owner")] public GitHubOwner? Owner { get; set; }
}

public class GitHubOwner
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
}

public class GitHubUser
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("id")] public long Id { get; set; }
}

public class RepoInvitation
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("repository")] public GitHubRepo? Repository { get; set; }
    [JsonPropertyName("inviter")] public GitHubOwner? Inviter { get; set; }
}

// Respuestas del device flow OAuth
public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
    [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
}

public class AccessTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}
