using SharedKernel;

namespace Domain.Users.Identity;

/// <summary>
/// A validated identity document. Follows parse-don't-validate: an instance can only exist
/// once its number has passed the validator for its <see cref="IdentityDocumentType"/>,
/// so downstream code never has to re-check validity.
/// </summary>
public sealed record IdentityDocument
{
    private IdentityDocument(IdentityDocumentType type, string value)
    {
        Type = type;
        Value = value;
    }

    public IdentityDocumentType Type { get; }

    public string Value { get; }

    public static Result<IdentityDocument> Create(
        IdentityDocumentType type,
        string? value,
        IIdentityDocumentValidatorResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        if (!resolver.Resolve(type).IsValid(value))
        {
            return Result.Failure<IdentityDocument>(UserErrors.InvalidIdentityDocument);
        }

        return Result.Success(new IdentityDocument(type, value!.Trim()));
    }
}
