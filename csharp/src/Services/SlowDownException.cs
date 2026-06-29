namespace EntregaEvaluacion.Services;

/// <summary>
/// Lanzada cuando GitHub responde "slow_down" en el device flow. El caller
/// (LoginWindow) debe aumentar el intervalo del timer en AddSeconds segun
/// la spec de OAuth 2.0 device flow (rfc 8628 sec 3.5).
///
/// Tipo top-level (antes anidado en GitHubService) para que el seam
/// IGitHubService sea consumible sin depender de la clase concreta: el caller
/// captura SlowDownException sin cualificar por GitHubService.
/// </summary>
public class SlowDownException : Exception
{
    public int AddSeconds { get; }
    public SlowDownException(int add) : base($"GitHub pidio ir mas lento (+{add}s)")
        => AddSeconds = add;
}
