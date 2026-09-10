using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Security;

public sealed class FieldEncryptionOptions
{
    public const string SectionName = "FieldEncryption";

    [Required]
    public string Key { get; init; } = string.Empty;
}
