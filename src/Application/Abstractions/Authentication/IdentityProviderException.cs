using System.Net;

namespace Application.Abstractions.Authentication;

/// <summary>
/// Raised when the identity provider returns an error or is unreachable. Carries the HTTP status
/// and, when present, the standard OAuth2 <c>error</c> code from the response body (e.g.
/// <c>invalid_grant</c>). Handlers key off the OAuth error to classify auth-domain outcomes
/// independently of the HTTP status, since providers return e.g. bad credentials as 400 or 401.
/// </summary>
public sealed class IdentityProviderException(
    HttpStatusCode statusCode,
    string message,
    string? error = null) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>The OAuth2 <c>error</c> code (e.g. <c>invalid_grant</c>), or null if none was parsed.</summary>
    public string? Error { get; } = error;
}
