using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Domain.AuditLogs;
using Domain.Statements.Events;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Features.Statements.Upload;

internal sealed class StatementUploadedDomainEventHandler(
    IApplicationDbContext context,
    INotificationService notificationService,
    ILogger<StatementUploadedDomainEventHandler> logger)
    : IDomainEventHandler<StatementUploadedDomainEvent>
{
    public async Task Handle(StatementUploadedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        bool alreadyNotified = await context.AuditLogs
            .AsNoTracking()
            .AnyAsync(
                a => a.StatementId == domainEvent.StatementId &&
                     a.Action == AuditAction.StatementAvailableNotified,
                cancellationToken);

        if (alreadyNotified)
        {
            return;
        }

        string? period = await context.Statements
            .AsNoTracking()
            .Where(s => s.Id == domainEvent.StatementId)
            .Select(s => s.Period)
            .FirstOrDefaultAsync(cancellationToken);

        if (period is null)
        {
            logger.LogWarning(
                "Statement {StatementId} no longer exists; skipping availability notification.",
                domainEvent.StatementId);
            return;
        }

        await notificationService.NotifyStatementAvailableAsync(
            new StatementAvailableNotification(
                domainEvent.CustomerId,
                domainEvent.StatementId,
                period,
                StatementMessages.PasswordHint),
            cancellationToken);

        context.AuditLogs.Add(AuditLog.Create(
            domainEvent.StatementId,
            domainEvent.CustomerId,
            AuditAction.StatementAvailableNotified));

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Queued 'statement available' notification for customer {CustomerId}, statement {StatementId} ({Period}).",
            domainEvent.CustomerId,
            domainEvent.StatementId,
            period);
    }
}
