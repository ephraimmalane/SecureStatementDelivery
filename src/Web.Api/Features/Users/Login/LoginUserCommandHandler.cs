using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Domain.Users;
using SharedKernel;
using System.Net;

namespace Web.Api.Features.Users.Login;

internal sealed class LoginUserCommandHandler(IIdentityProviderClient identityProvider, TimeProvider timeProvider)
    : ICommandHandler<LoginUserCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(LoginUserCommand command, CancellationToken cancellationToken)
    {
        try
        {
            AuthenticationResult token = await identityProvider.LoginAsync(
                command.Email,
                command.Password,
                cancellationToken);

            return Result.Success(new LoginResponse(
                token.AccessToken,
                token.RefreshToken,
                timeProvider.GetUtcNow().UtcDateTime.AddSeconds(token.ExpiresInSeconds)));
        }
        catch (IdentityProviderException ex) when (
            ex.Error == "invalid_grant" || ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return Result.Failure<LoginResponse>(UserErrors.InvalidCredentials);
        }
        catch (IdentityProviderException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return Result.Failure<LoginResponse>(UserErrors.AccountInactive);
        }
        
    }
}
