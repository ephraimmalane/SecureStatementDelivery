namespace Domain.Users.Identity;

/// <summary>
/// Adapts the existing <see cref="SouthAfricanIdValidator"/> to the <see cref="IIdentityDocumentValidator"/>
/// seam. The validation rules (length, date of birth, citizenship digit, Luhn checksum) are unchanged.
/// </summary>
internal sealed class SouthAfricanIdDocumentValidator : IIdentityDocumentValidator
{
    public IdentityDocumentType Type => IdentityDocumentType.SouthAfricanId;

    public bool IsValid(string? documentNumber) =>
        SouthAfricanIdValidator.IsValid(documentNumber);
}
