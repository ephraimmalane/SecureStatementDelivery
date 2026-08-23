namespace Infrastructure.Security;

public sealed class FieldEncryptionOptions
{
    public const string SectionName = "FieldEncryption";

    public string Key { get; init; } = string.Empty;
}
