using Application.Abstractions.Messaging;
using Web.Api.Features.Users.Login;

namespace Web.Api.Features.Users.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<LoginResponse>;
