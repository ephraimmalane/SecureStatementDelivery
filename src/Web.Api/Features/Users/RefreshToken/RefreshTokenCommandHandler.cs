using Application.Abstractions.Messaging;
using Domain.Users;
using Infrastructure.Keycloak;
using SharedKernel;
using System.Net;
using Web.Api.Features.Users.Login;

namespace Web.Api.Features.Users.RefreshToken;

internal sealed class RefreshTokenCommandHandler(IKeycloakClient keycloakClient)
    : ICommandHandler<RefreshTokenCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        try
        {
            // Keycloak manages refresh token rotation and family invalidation natively.
            KeycloakTokenResponse token = await keycloakClient.RefreshTokenAsync(
                command.RefreshToken,
                cancellationToken);

            return Result.Success(new LoginResponse(
                token.AccessToken,
                token.RefreshToken,
                DateTime.UtcNow.AddSeconds(token.ExpiresIn)));
        }
        catch (KeycloakAuthException ex) when (
            ex.StatusCode == HttpStatusCode.Unauthorized ||
            ex.StatusCode == HttpStatusCode.BadRequest)
        {
            return Result.Failure<LoginResponse>(UserErrors.InvalidRefreshToken);
        }
    }
}
