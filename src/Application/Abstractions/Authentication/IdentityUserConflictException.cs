using SharedKernel;

namespace Application.Abstractions.Authentication;

/// <summary>
/// Raised when the identity provider already holds a user that conflicts with the registration
/// request. Carries the domain <see cref="Error"/> to return once reconciliation is exhausted.
/// </summary>
public sealed class IdentityUserConflictException(Error domainError) : Exception(domainError.Description)
{
    public Error DomainError { get; } = domainError;
}
