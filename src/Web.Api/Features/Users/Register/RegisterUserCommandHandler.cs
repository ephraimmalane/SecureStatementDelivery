using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Web.Api.Features.Users.Register;

internal sealed class RegisterUserCommandHandler(
    IApplicationDbContext context,
    IIdentityProviderClient identityProvider,
    ILogger<RegisterUserCommandHandler> logger) : ICommandHandler<RegisterUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        Guid keycloakId;

        try
        {
            keycloakId = await identityProvider.RegisterUserAsync(
                command.Email,
                command.FirstName,
                command.LastName,
                command.Password,
                cancellationToken);
        }
        catch (IdentityUserConflictException ex)
        {
            return await ReconcileOrphanedRegistrationAsync(command, ex, cancellationToken);
        }

        Result<User> userResult = User.Create(
            keycloakId,
            command.Email,
            command.FirstName,
            command.LastName,
            command.SouthAfricanIdNumber);

        if (userResult.IsFailure)
        {
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
            if (await LocalUserIsConfirmedAbsentAsync(user.Id, saveEx))
            {
                await TryDeleteKeycloakUserAsync(user.Id);
            }

            throw;
        }
    }

    private async Task<Result<Guid>> ReconcileOrphanedRegistrationAsync(
        RegisterUserCommand command,
        IdentityUserConflictException conflict,
        CancellationToken cancellationToken)
    {
        bool localMirrorExists = await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == command.Email, cancellationToken);

        if (localMirrorExists)
        {
            return Result.Failure<Guid>(conflict.DomainError);
        }

        Guid? keycloakId = await identityProvider.GetUserIdByEmailAsync(command.Email, cancellationToken);
        if (keycloakId is null)
        {
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

    private async Task<bool> LocalUserIsConfirmedAbsentAsync(Guid userId, Exception saveEx)
    {
        try
        {
            return !await context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == userId, CancellationToken.None);
        }
        catch (Exception verifyEx)
        {
            logger.LogError(
                verifyEx,
                "Could not verify local persistence for {UserId} after save error '{Err}'; " +
                "keeping the identity-provider identity.",
                userId,
                saveEx.Message);
            return false;
        }
    }

    private async Task TryDeleteKeycloakUserAsync(Guid userId)
    {
        try
        {
            await identityProvider.DeleteUserAsync(userId, CancellationToken.None);
        }
        catch (Exception rollbackEx)
        {
            logger.LogError(
                rollbackEx,
                "Failed to roll back orphaned identity-provider user {UserId}; requires reconciliation.",
                userId);
        }
    }
}
