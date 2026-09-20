namespace Application.Abstractions.Notifications;

public interface INotificationService
{
    Task NotifyStatementAvailableAsync(
        StatementAvailableNotification notification,
        CancellationToken cancellationToken);

    Task NotifyUserRegisteredAsync(
        WelcomeNotification notification,
        CancellationToken cancellationToken);
}
