namespace Infrastructure.Authentication;

public sealed class DownloadTokenOptions
{
    public const string SectionName = "DownloadToken";

    public string Secret { get; init; } = string.Empty;

    public string Issuer { get; init; } = "statement-download";

    public string Audience { get; init; } = "statement-download-clients";
}
