namespace Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; init; } = 10;

    public int BatchSize { get; init; } = 20;
}
