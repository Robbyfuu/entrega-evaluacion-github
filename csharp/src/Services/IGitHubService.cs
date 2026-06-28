using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Espejo 1:1 de la superficie publica de instancia de GitHubService:
/// autenticacion via device flow OAuth + llamadas a la API REST de GitHub.
/// Interfaz transitoria (sin inyeccion todavia) para que los consumidores
/// dependan de la abstraccion en lugar de la clase concreta. La construccion
/// sigue siendo new GitHubService(). Los miembros estaticos y el tipo anidado
/// SlowDownException NO forman parte del contrato (siguen accesibles por el
/// nombre concreto).
/// </summary>
public interface IGitHubService
{
    // ===== Estado de sesion =====
    // True si hay token en memoria.
    bool IsAuthenticated { get; }
    // Token vigente (null si no hay sesion).
    string? Token { get; }

    // Cierra sesion: limpia token en memoria, headers y archivo en disco.
    void Logout();

    // ===== Device flow =====
    // Solicita un device code a GitHub para iniciar el flow.
    Task<DeviceCodeResponse?> RequestDeviceCodeAsync();
    // Poll del token: devuelve token si autorizado, null si pendiente, lanza si fatal.
    Task<string?> PollAccessTokenAsync(string deviceCode);

    // ===== API =====
    // Datos del usuario autenticado.
    Task<GitHubUser?> GetUserAsync();
    // Repos del alumno (owner + colaborador + organizacion).
    Task<List<GitHubRepo>> ListReposAsync();
    // Un repo concreto por owner/nombre.
    Task<GitHubRepo?> GetRepoAsync(string owner, string repo);
    // Invitaciones de repo pendientes (null en error, NO lista vacia).
    Task<List<RepoInvitation>?> GetPendingInvitationsAsync();
    // Acepta una invitacion de repo por id.
    Task<bool> AcceptInvitationAsync(long invitationId);
    // Crea un repo nuevo (privado salvo isPublic=true).
    Task<bool> CreateRepoAsync(string name, bool isPublic);
}
