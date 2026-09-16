using Application.Abstractions.Notifications;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Notifications;

/// <summary>
/// Fallback used when no notification queue is configured (local/dev/test). It records intent
/// without sending anything, so the app runs without notification infrastructure. Production
/// configures <c>Notifications:QueueUrl</c> to get <see cref="SqsNotificationService"/>.
/// </summary>
internal sealed class LoggingNotificationService(ILogger<LoggingNotificationService> logger)
    : INotificationService
{
    public Task NotifyStatementAvailableAsync(
        StatementAvailableNotification notification,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Notifications not configured — would queue 'statement available' for customer " +
            "{CustomerId}, statement {StatementId} ({Period}). No document or link is ever included.",
            notification.CustomerId,
            notification.StatementId,
            notification.Period);

        return Task.CompletedTask;
    }

    public Task NotifyUserRegisteredAsync(
        WelcomeNotification notification,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Notifications not configured — would queue 'welcome' for customer {CustomerId}.",
            notification.CustomerId);

        return Task.CompletedTask;
    }
}
