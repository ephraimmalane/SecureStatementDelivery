namespace Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>SQS queue that a downstream consumer drains to send the actual message.</summary>
    public string? QueueUrl { get; init; }

    public string? Region { get; init; }

    public string? ServiceUrl { get; init; }
}
