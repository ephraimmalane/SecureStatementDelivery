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

    // Compensating action: removes a user from Keycloak. Used to roll back a Keycloak
    // registration when the subsequent local persistence fails, so the two stores never drift.
    Task DeleteUserAsync(Guid keycloakUserId, CancellationToken cancellationToken);
}
