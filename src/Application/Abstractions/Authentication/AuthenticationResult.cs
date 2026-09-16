namespace Application.Abstractions.Authentication;

/// <summary>
/// Vendor-neutral tokens returned by <see cref="IIdentityProviderClient"/>. The provider's
/// wire format stays an Infrastructure detail and is mapped onto this type at the boundary.
/// </summary>
public sealed record AuthenticationResult(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds);
