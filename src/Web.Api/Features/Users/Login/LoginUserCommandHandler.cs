using Application.Abstractions.Messaging;
using Domain.Users;
using Infrastructure.Keycloak;
using SharedKernel;
using System.Net;

namespace Web.Api.Features.Users.Login;

internal sealed class LoginUserCommandHandler(IKeycloakClient keycloakClient)
    : ICommandHandler<LoginUserCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(LoginUserCommand command, CancellationToken cancellationToken)
    {
        try
        {
            KeycloakTokenResponse token = await keycloakClient.LoginAsync(
                command.Email,
                command.Password,
                cancellationToken);

            return new LoginResponse(
                token.AccessToken,
                token.RefreshToken,
                DateTime.UtcNow.AddSeconds(token.ExpiresIn));
        }
        catch (KeycloakAuthException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return Result.Failure<LoginResponse>(UserErrors.InvalidCredentials);
        }
        catch (KeycloakAuthException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return Result.Failure<LoginResponse>(UserErrors.AccountInactive);
        }
    }
}
