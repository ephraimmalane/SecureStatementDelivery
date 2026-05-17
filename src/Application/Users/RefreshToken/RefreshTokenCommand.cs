using Application.Abstractions.Messaging;
using Application.Users.Login;

namespace Application.Users.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<LoginResponse>;
