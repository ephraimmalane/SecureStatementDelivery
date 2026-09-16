namespace Application.Abstractions.Authentication;

/// <summary>
/// Port for the external identity provider (implemented in Infrastructure by the Keycloak client).
/// Keeps use-case handlers free of any vendor- or transport-specific dependency.
/// </summary>
public interface IIdentityProviderClient
{
    Task<AuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken);

    Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task<Guid> RegisterUserAsync(
        string email,
        string firstName,
        string lastName,
        string password,
        CancellationToken cancellationToken);

    Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
}
