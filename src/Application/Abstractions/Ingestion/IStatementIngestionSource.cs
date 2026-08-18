namespace Application.Abstractions.Ingestion;

// A pull-based source of statements produced out-of-band by the bank's statement-generation
// pipeline — typically an object-storage (S3) event delivered onto a queue (SQS). The worker polls
// ReceiveAsync, feeds each message through the ingestion funnel, and only then AcknowledgeAsync's
// it. A message left unacknowledged (because ingestion failed) is redelivered by the queue and
// eventually dead-lettered, which is safe because the funnel is idempotent on DocumentId.
public interface IStatementIngestionSource
{
    // Returns the next batch of pending statements, or an empty list if none are available. May
    // block up to the source's long-poll window.
    Task<IReadOnlyList<StatementIngestionMessage>> ReceiveAsync(CancellationToken cancellationToken);

    // Marks a message as durably handled so it is not redelivered.
    Task AcknowledgeAsync(StatementIngestionMessage message, CancellationToken cancellationToken);
}
