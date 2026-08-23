namespace Infrastructure.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public bool Enabled { get; init; }

    public string QueueUrl { get; init; } = string.Empty;

    public int MaxMessagesPerPoll { get; init; } = 10;
    public int WaitTimeSeconds { get; init; } = 20;

    public int EmptyPollDelaySeconds { get; init; } = 5;
    public int ErrorBackoffSeconds { get; init; } = 15;

    public string Region { get; init; } = string.Empty;
    public string? ServiceUrl { get; init; }
}
