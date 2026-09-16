using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Domain.Users;
using SharedKernel;
using System.Net;
using Web.Api.Features.Users.Login;

namespace Web.Api.Features.Users.RefreshToken;

internal sealed class RefreshTokenCommandHandler(IIdentityProviderClient identityProvider, TimeProvider timeProvider)
    : ICommandHandler<RefreshTokenCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        try
        {
            AuthenticationResult token = await identityProvider.RefreshTokenAsync(
                command.RefreshToken,
                cancellationToken);

            return Result.Success(new LoginResponse(
                token.AccessToken,
                token.RefreshToken,
                timeProvider.GetUtcNow().UtcDateTime.AddSeconds(token.ExpiresInSeconds)));
        }
        catch (IdentityProviderException ex) when (
            ex.StatusCode == HttpStatusCode.Unauthorized ||
            ex.StatusCode == HttpStatusCode.BadRequest)
        {
            return Result.Failure<LoginResponse>(UserErrors.InvalidRefreshToken);
        }
    }
}
