namespace Infrastructure.Outbox;

// A domain event persisted in the same transaction as the state change that raised it
// (the transactional outbox pattern). The OutboxProcessor drains these asynchronously, so an
// event can never be lost when the process crashes between the DB commit and event dispatch.
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    // Assembly-qualified type name of the domain event, used to rehydrate it for dispatch.
    public string Type { get; init; } = string.Empty;

    // JSON-serialized domain event payload.
    public string Content { get; init; } = string.Empty;

    public DateTime OccurredOnUtc { get; init; }

    // Null until the processor has dispatched it. Set even on failure (with Error populated)
    // so a poison message does not block the queue forever.
    public DateTime? ProcessedOnUtc { get; set; }

    public string? Error { get; set; }
}
