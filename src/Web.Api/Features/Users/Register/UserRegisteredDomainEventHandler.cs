using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Domain.AuditLogs;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Features.Users.Register;

internal sealed class UserRegisteredDomainEventHandler(
    IApplicationDbContext context,
    INotificationService notificationService,
    ILogger<UserRegisteredDomainEventHandler> logger)
    : IDomainEventHandler<UserRegisteredDomainEvent>
{
    public async Task Handle(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        bool alreadyNotified = await context.AuditLogs
            .AsNoTracking()
            .AnyAsync(
                a => a.UserId == domainEvent.UserId &&
                     a.Action == AuditAction.WelcomeNotified,
                cancellationToken);

        if (alreadyNotified)
        {
            return;
        }

        bool userExists = await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == domainEvent.UserId, cancellationToken);

        if (!userExists)
        {
            logger.LogWarning(
                "User {UserId} no longer exists; skipping welcome notification.",
                domainEvent.UserId);
            return;
        }

        await notificationService.NotifyUserRegisteredAsync(
            new WelcomeNotification(domainEvent.UserId),
            cancellationToken);

        context.AuditLogs.Add(AuditLog.Create(
            statementId: null,
            userId: domainEvent.UserId,
            action: AuditAction.WelcomeNotified));

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Queued welcome notification for customer {UserId}.",
            domainEvent.UserId);
    }
}
