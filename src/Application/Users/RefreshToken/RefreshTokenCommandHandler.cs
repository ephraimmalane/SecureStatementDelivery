using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Users.Login;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Users.RefreshToken;

internal sealed class RefreshTokenCommandHandler(
    IApplicationDbContext context,
    ITokenProvider tokenProvider,
    IOptions<TokenOptions> tokenOptions) : ICommandHandler<RefreshTokenCommand, LoginResponse>
{
    private readonly TokenOptions _options = tokenOptions.Value;

    public async Task<Result<LoginResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        string tokenHash = ComputeHash(command.RefreshToken);

        Domain.Users.RefreshToken? existing = await context.RefreshTokens
            .Include(rt => rt.User).ThenInclude(u => u.Role)
            .SingleOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (existing is null || !existing.IsActive)
        {
            return Result.Failure<LoginResponse>(UserErrors.InvalidRefreshToken);
        }

        if (!existing.User.IsActive)
        {
            return Result.Failure<LoginResponse>(UserErrors.AccountInactive);
        }

        existing.Revoke();

        string newAccessToken = tokenProvider.Create(existing.User);
        string newRawRefreshToken = tokenProvider.GenerateRefreshToken();

        DateTime expiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenExpirationDays);
        string newTokenHash = ComputeHash(newRawRefreshToken);
        var newRefreshToken = Domain.Users.RefreshToken.Create(existing.UserId, newTokenHash, expiresAt);

        context.RefreshTokens.Add(newRefreshToken);
        await context.SaveChangesAsync(cancellationToken);

        return new LoginResponse(
            newAccessToken,
            newRawRefreshToken,
            DateTime.UtcNow.AddMinutes(_options.ExpirationInMinutes));
    }

    private static string ComputeHash(string token)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
