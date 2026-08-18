using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Infrastructure.Keycloak;
using Microsoft.EntityFrameworkCore;
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
            // Keycloak first: the identity must exist and be confirmed before we ever persist the
            // local mirror. This ordering is what guarantees the invariant "a local user never
            // exists without a Keycloak user" — we only reach the local write once Keycloak succeeded.
            keycloakId = await keycloakClient.RegisterUserAsync(
                command.Email,
                command.FirstName,
                command.LastName,
                command.Password,
                cancellationToken);
        }
        catch (KeycloakRegistrationException ex)
        {
            // Keycloak already has this email. Either it is a genuine duplicate, or a prior attempt
            // created the identity but failed to persist the local mirror — in which case we complete
            // that half now so the retry converges instead of dead-ending on "email already in use".
            return await ReconcileOrphanedRegistrationAsync(command, ex, cancellationToken);
        }

        // Mirror the user locally so statements can reference a CustomerId. Id == keycloakId so the
        // domain id matches the JWT subject claim. Create re-validates the SA ID as defence in depth
        // (FluentValidation already checked it upstream).
        Result<User> userResult = User.Create(
            keycloakId,
            command.Email,
            command.FirstName,
            command.LastName,
            command.SouthAfricanIdNumber);

        if (userResult.IsFailure)
        {
            // The Keycloak identity exists but the domain rejected the input, so nothing was persisted
            // locally. Roll the identity back to preserve the "a local user always has a Keycloak
            // identity" invariant.
            await TryDeleteKeycloakUserAsync(keycloakId);
            return Result.Failure<Guid>(userResult.Error);
        }

        User user = userResult.Value;
        context.Users.Add(user);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(user.Id);
        }
        catch (Exception saveEx)
        {
            // Local persistence failed. We must NEVER leave a local user without a Keycloak identity,
            // so we delete the Keycloak identity ONLY if we can prove the local row never persisted;
            // any doubt keeps it, so we can never orphan a local user.
            if (await LocalUserIsConfirmedAbsentAsync(user.Id, saveEx))
            {
                await TryDeleteKeycloakUserAsync(user.Id);
            }

            throw;
        }
    }

    // Handles a Keycloak email conflict. If a local mirror already exists this is a genuine duplicate
    // registration; otherwise a prior attempt left an orphaned Keycloak identity (present in Keycloak,
    // missing locally) and we finish persisting the local half so the retry is idempotent. The
    // pre-existing identity is only ever completed here — never deleted.
    private async Task<Result<Guid>> ReconcileOrphanedRegistrationAsync(
        RegisterUserCommand command,
        KeycloakRegistrationException conflict,
        CancellationToken cancellationToken)
    {
        bool localMirrorExists = await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == command.Email, cancellationToken);

        if (localMirrorExists)
        {
            // The account is already fully registered on both sides.
            return Result.Failure<Guid>(conflict.DomainError);
        }

        // No local mirror: recover the orphaned identity's id so the local User can be created with the
        // matching Keycloak subject.
        Guid? keycloakId = await keycloakClient.GetUserIdByEmailAsync(command.Email, cancellationToken);
        if (keycloakId is null)
        {
            // The identity is gone (e.g. concurrently cleaned up between the conflict and now); surface
            // the original conflict.
            return Result.Failure<Guid>(conflict.DomainError);
        }

        Result<User> userResult = User.Create(
            keycloakId.Value,
            command.Email,
            command.FirstName,
            command.LastName,
            command.SouthAfricanIdNumber);

        if (userResult.IsFailure)
        {
            // Do not delete the pre-existing Keycloak identity; just report the validation failure.
            return Result.Failure<Guid>(userResult.Error);
        }

        context.Users.Add(userResult.Value);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(userResult.Value.Id);
        }
        catch (DbUpdateException)
        {
            // A concurrent retry completed the mirror first (unique email / primary key). If the row
            // now exists, treat as success — the registration is idempotent. The pre-existing Keycloak
            // identity is never rolled back on this path.
            Guid? existingId = await context.Users
                .AsNoTracking()
                .Where(u => u.Email == command.Email)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingId is not null)
            {
                return Result.Success(existingId.Value);
            }

            throw;
        }
    }

    // Returns true only when the database positively confirms the row is absent. Any error (e.g. the
    // database is still unreachable, so we cannot tell whether the commit landed) → false → we keep
    // Keycloak, preserving the "a local user always has a Keycloak identity" invariant.
    private async Task<bool> LocalUserIsConfirmedAbsentAsync(Guid userId, Exception saveEx)
    {
        try
        {
            // Hits the database (the failed Added entity is not yet persisted, so it is not returned)
            // with a non-cancellable token so a request cancellation cannot abort the check.
            // AsNoTracking avoids touching the faulted change-tracker state.
            return !await context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == userId, CancellationToken.None);
        }
        catch (Exception verifyEx)
        {
            logger.LogError(
                verifyEx,
                "Could not verify local persistence for {UserId} after save error '{Err}'; " +
                "keeping the Keycloak identity.",
                userId,
                saveEx.Message);
            return false;
        }
    }

    // Best-effort compensation: only invoked once the local row is confirmed absent. Swallows and logs
    // its own failure so it can never mask the original save error being rethrown by the caller.
    private async Task TryDeleteKeycloakUserAsync(Guid userId)
    {
        try
        {
            await keycloakClient.DeleteUserAsync(userId, CancellationToken.None);
        }
        catch (Exception rollbackEx)
        {
            logger.LogError(
                rollbackEx,
                "Failed to roll back orphaned Keycloak user {UserId}; requires reconciliation.",
                userId);
        }
    }
}
