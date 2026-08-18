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

    // Looks up an existing Keycloak user's id by exact email, or null if none exists. Used to
    // reconcile an orphaned identity (present in Keycloak, missing locally) so a retried registration
    // can complete the local half instead of dead-ending on an email conflict.
    Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    // Compensating action: removes a user from Keycloak. Used to roll back a Keycloak
    // registration when the subsequent local persistence fails, so the two stores never drift.
    Task DeleteUserAsync(Guid keycloakUserId, CancellationToken cancellationToken);
}
