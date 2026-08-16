using Application.Abstractions.Messaging;

namespace Web.Api.Features.Users.Login;

public sealed record LoginUserCommand(string Email, string Password) : ICommand<LoginResponse>
{
    // Records auto-generate ToString() that includes every property.
    // Override so passwords never appear in logs, exception messages, or test output.
#pragma warning disable S2068 // False positive: "[REDACTED]" is not a hardcoded credential.
    public override string ToString() => $"LoginUserCommand {{ Email = {Email}, Password = [REDACTED] }}";
#pragma warning restore S2068
}

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt);
