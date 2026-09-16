using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.SQS;
using Amazon.SQS.Model;
using Application.Abstractions.Notifications;
using Microsoft.Extensions.Options;

namespace Infrastructure.Notifications;

/// <summary>
/// Publishes a "statement available" message to SQS. The actual channel send (email/SMS/push) is a
/// downstream consumer's job; keeping this a fast enqueue means the outbox dispatch that calls it is
/// not blocked on provider latency. The consumer is expected to dedupe on <c>StatementId</c>, since
/// enqueue is at-least-once.
/// </summary>
internal sealed class SqsNotificationService(
    IAmazonSQS sqs,
    IOptions<NotificationOptions> options) : INotificationService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NotificationOptions _options = options.Value;

    public Task NotifyStatementAvailableAsync(
        StatementAvailableNotification notification,
        CancellationToken cancellationToken)
    {
        var message = new StatementAvailableMessage(
            notification.CustomerId,
            notification.StatementId,
            notification.Period,
            notification.PasswordHint);

        return sqs.SendMessageAsync(
            new SendMessageRequest
            {
                QueueUrl = _options.QueueUrl,
                MessageBody = JsonSerializer.Serialize(message, JsonOptions)
            },
            cancellationToken);
    }

    public Task NotifyUserRegisteredAsync(
        WelcomeNotification notification,
        CancellationToken cancellationToken)
    {
        var message = new WelcomeMessage(notification.CustomerId);

        return sqs.SendMessageAsync(
            new SendMessageRequest
            {
                QueueUrl = _options.QueueUrl,
                MessageBody = JsonSerializer.Serialize(message, JsonOptions)
            },
            cancellationToken);
    }

    private sealed record StatementAvailableMessage(
        [property: JsonPropertyName("customerId")] Guid CustomerId,
        [property: JsonPropertyName("statementId")] Guid StatementId,
        [property: JsonPropertyName("period")] string Period,
        [property: JsonPropertyName("passwordHint")] string PasswordHint);

    private sealed record WelcomeMessage(
        [property: JsonPropertyName("customerId")] Guid CustomerId);
}
