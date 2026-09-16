namespace Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; init; } = 10;

    public int BatchSize { get; init; } = 20;

    public int MaxRetries { get; init; } = 5;

    public int BaseRetryDelaySeconds { get; init; } = 10;

    public int MaxRetryDelaySeconds { get; init; } = 600;
}
