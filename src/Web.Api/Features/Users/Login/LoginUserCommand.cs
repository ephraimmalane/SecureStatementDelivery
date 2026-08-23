using Application.Abstractions.Messaging;

namespace Web.Api.Features.Users.Login;

public sealed record LoginUserCommand(string Email, string Password) : ICommand<LoginResponse>
{
#pragma warning disable S2068
    public override string ToString() => $"LoginUserCommand {{ Email = {Email}, Password = [REDACTED] }}";
#pragma warning restore S2068
}

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt);
