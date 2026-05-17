namespace Application.Abstractions.Authentication;

public sealed class TokenOptions
{
    public const string SectionName = "Jwt";

    public int ExpirationInMinutes { get; init; } = 60;

    public int RefreshTokenExpirationDays { get; init; } = 30;
}
