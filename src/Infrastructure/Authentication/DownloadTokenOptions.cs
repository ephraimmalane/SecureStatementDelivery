using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Authentication;

public sealed class DownloadTokenOptions
{
    public const string SectionName = "DownloadToken";

    [Required]
    [MinLength(32, ErrorMessage = "DownloadToken:Secret must be at least 32 characters (256 bits).")]
    public string Secret { get; init; } = string.Empty;

    public string[] PreviousSecrets { get; init; } = [];

    [Required]
    public string Issuer { get; init; } = "statement-download";

    [Required]
    public string Audience { get; init; } = "statement-download-clients";
}
