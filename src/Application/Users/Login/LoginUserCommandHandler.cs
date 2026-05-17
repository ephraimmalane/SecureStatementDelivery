using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Users.Login;

internal sealed class LoginUserCommandHandler(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher,
    ITokenProvider tokenProvider,
    IOptions<TokenOptions> tokenOptions) : ICommandHandler<LoginUserCommand, LoginResponse>
{
    private readonly TokenOptions _options = tokenOptions.Value;

    public async Task<Result<LoginResponse>> Handle(LoginUserCommand command, CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Email == command.Email, cancellationToken);

        if (user is null || !passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return Result.Failure<LoginResponse>(UserErrors.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            return Result.Failure<LoginResponse>(UserErrors.AccountInactive);
        }

        string accessToken = tokenProvider.Create(user);
        string rawRefreshToken = tokenProvider.GenerateRefreshToken();

        DateTime expiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenExpirationDays);
        string tokenHash = ComputeHash(rawRefreshToken);
        var refreshToken = Domain.Users.RefreshToken.Create(user.Id, tokenHash, expiresAt);

        context.RefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync(cancellationToken);

        return new LoginResponse(
            accessToken,
            rawRefreshToken,
            DateTime.UtcNow.AddMinutes(_options.ExpirationInMinutes));
    }

    private static string ComputeHash(string token)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
