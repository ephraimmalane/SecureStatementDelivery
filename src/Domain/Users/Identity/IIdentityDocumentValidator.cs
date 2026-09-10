namespace Domain.Users.Identity;

/// <summary>
/// Validates the format of a single identity-document scheme (e.g. a South African ID number).
/// One implementation per <see cref="IdentityDocumentType"/>; new document types are added
/// by introducing a new implementation, without modifying existing ones.
/// </summary>
public interface IIdentityDocumentValidator
{
    IdentityDocumentType Type { get; }

    bool IsValid(string? documentNumber);
}
