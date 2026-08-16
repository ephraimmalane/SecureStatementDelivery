namespace Application.Abstractions.Ingestion;

// A single statement waiting to be ingested, surfaced by an IStatementIngestionSource (e.g. an S3
// event delivered via SQS). It carries the metadata the funnel needs plus a lazy accessor for the
// bytes, so a large PDF is only fetched from object storage when the processor is ready to stream
// it — not eagerly during polling. ReceiptHandle is an opaque provider token used to acknowledge
// (delete) the message once the statement has been durably created.
public sealed class StatementIngestionMessage
{
    public required Guid CustomerId { get; init; }

    // Canonical YYYY-MM statement period (validated downstream by the domain invariant).
    public required string Period { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    // Mandatory: ingestion is at-least-once, so the funnel deduplicates redeliveries on this key.
    public required string IdempotencyKey { get; init; }

    public string? Description { get; init; }

    // Opens the PDF bytes on demand (e.g. an S3 GetObject stream). Called once, when the processor
    // is about to hand the content to the funnel.
    public required Func<CancellationToken, Task<Stream>> OpenContentAsync { get; init; }

    // Provider-specific acknowledgement token (e.g. an SQS receipt handle), opaque to the processor.
    public required string ReceiptHandle { get; init; }
}
