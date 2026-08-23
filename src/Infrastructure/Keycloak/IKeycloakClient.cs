namespace Infrastructure.Keycloak;

public interface IKeycloakClient
{
    Task<KeycloakTokenResponse> LoginAsync(string email, string password, CancellationToken cancellationToken);

    Task<KeycloakTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task<Guid> RegisterUserAsync(
        string email,
        string firstName,
        string lastName,
        string password,
        CancellationToken cancellationToken);

    Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    Task DeleteUserAsync(Guid keycloakUserId, CancellationToken cancellationToken);
}
