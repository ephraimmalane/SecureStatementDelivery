namespace Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    // How often the processor polls for unprocessed messages.
    public int PollIntervalSeconds { get; init; } = 10;

    // Maximum messages claimed per poll. FOR UPDATE SKIP LOCKED makes this safe across replicas.
    public int BatchSize { get; init; } = 20;
}
