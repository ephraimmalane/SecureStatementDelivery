namespace Application.Abstractions.Notifications;

/// <summary>
/// Arranges delivery of customer notifications. Implementations are expected to hand off quickly
/// (e.g. enqueue a message) rather than call a slow notification provider inline, so callers running
/// inside a request or an outbox dispatch are not blocked on provider latency.
/// </summary>
public interface INotificationService
{
    Task NotifyStatementAvailableAsync(
        StatementAvailableNotification notification,
        CancellationToken cancellationToken);

    Task NotifyUserRegisteredAsync(
        WelcomeNotification notification,
        CancellationToken cancellationToken);
}
