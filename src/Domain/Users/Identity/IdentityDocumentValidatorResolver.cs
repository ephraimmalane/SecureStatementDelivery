namespace Domain.Users.Identity;

public sealed class IdentityDocumentValidatorResolver : IIdentityDocumentValidatorResolver
{
    private readonly Dictionary<IdentityDocumentType, IIdentityDocumentValidator> _validators;

    public IdentityDocumentValidatorResolver(IEnumerable<IIdentityDocumentValidator> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);

        _validators = validators.ToDictionary(validator => validator.Type);
    }

    public IIdentityDocumentValidator Resolve(IdentityDocumentType type) =>
        _validators.TryGetValue(type, out IIdentityDocumentValidator? validator)
            ? validator
            : throw new NotSupportedException(
                $"No identity-document validator is registered for '{type}'.");
}
