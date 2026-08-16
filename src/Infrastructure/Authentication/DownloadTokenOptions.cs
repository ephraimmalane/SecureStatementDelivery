namespace Infrastructure.Authentication;

public sealed class DownloadTokenOptions
{
    public const string SectionName = "DownloadToken";

    // The active signing key. GenerateToken always signs with this.
    public string Secret { get; init; } = string.Empty;

    // Previously-active signing keys, kept only for validation during a rotation overlap window.
    // A link signed before the rotation keeps working until its (short) TTL elapses; once the
    // overlap window has passed, drop the old key from here. New links are never signed with these.
    public string[] PreviousSecrets { get; init; } = [];

    public string Issuer { get; init; } = "statement-download";

    public string Audience { get; init; } = "statement-download-clients";
}
