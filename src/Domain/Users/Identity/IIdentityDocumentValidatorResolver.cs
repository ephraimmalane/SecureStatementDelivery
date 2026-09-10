namespace Domain.Users.Identity;

/// <summary>
/// Resolves the <see cref="IIdentityDocumentValidator"/> registered for a given
/// <see cref="IdentityDocumentType"/>.
/// </summary>
public interface IIdentityDocumentValidatorResolver
{
    IIdentityDocumentValidator Resolve(IdentityDocumentType type);
}
