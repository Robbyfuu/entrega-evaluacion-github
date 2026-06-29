using EntregaEvaluacion.Models;

namespace EntregaEvaluacion.Services;

/// <summary>
/// Espejo 1:1 de la superficie publica de instancia de GitHubService:
/// autenticacion via device flow OAuth + llamadas a la API REST de GitHub.
/// Interfaz transitoria (sin inyeccion todavia) para que los consumidores
/// dependan de la abstraccion en lugar de la clase concreta. La construccion
/// sigue siendo new GitHubService(). Los miembros estaticos NO forman parte del
/// contrato. La excepcion SlowDownException es ahora un tipo top-level del
/// namespace EntregaEvaluacion.Services (ya no anidada en la clase concreta),
/// asi el caller la captura sin depender de GitHubService.
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
    // Puede lanzar SlowDownException ("slow_down" de GitHub): el caller debe
    // aumentar el intervalo del poll (rfc 8628). Tambien TimeoutException /
    // UnauthorizedAccessException (fatales) y errores transitorios de red/JSON.
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
