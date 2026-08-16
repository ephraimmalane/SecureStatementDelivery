namespace Infrastructure.Security;

public sealed class FieldEncryptionOptions
{
    public const string SectionName = "FieldEncryption";

    // Base64-encoded 256-bit (32-byte) AES key. Supplied via secret management, never committed.
    public string Key { get; init; } = string.Empty;
}
