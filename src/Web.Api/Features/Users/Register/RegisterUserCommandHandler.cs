using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Infrastructure.Keycloak;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Web.Api.Features.Users.Register;

internal sealed class RegisterUserCommandHandler(
    IApplicationDbContext context,
    IKeycloakClient keycloakClient,
    ILogger<RegisterUserCommandHandler> logger) : ICommandHandler<RegisterUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        Guid keycloakId;

        try
        {
            // Create the user in Keycloak; the returned UUID becomes our domain User.Id.
            keycloakId = await keycloakClient.RegisterUserAsync(
                command.Email,
                command.FirstName,
                command.LastName,
                command.Password,
                cancellationToken);
        }
        catch (KeycloakRegistrationException ex)
        {
            return Result.Failure<Guid>(ex.DomainError);
        }

        try
        {
            // Mirror the user locally so statements can reference a CustomerId.
            var user = User.Create(
                keycloakId,
                command.Email,
                command.FirstName,
                command.LastName,
                command.SouthAfricanIdNumber);
            context.Users.Add(user);
            await context.SaveChangesAsync(cancellationToken);

            return user.Id;
        }
        catch
        {
            // The Keycloak user was created but local persistence failed. Roll back the
            // Keycloak side so the two stores never drift into an orphaned account.
            // Use a non-cancellable token so the compensation completes even if the request
            // was cancelled.
            try
            {
                await keycloakClient.DeleteUserAsync(keycloakId, CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                logger.LogError(
                    rollbackEx,
                    "Failed to roll back Keycloak user {KeycloakUserId} after local persistence failed; " +
                    "the account may be orphaned and require manual cleanup.",
                    keycloakId);
            }

            throw;
        }
    }
}
