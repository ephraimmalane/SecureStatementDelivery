namespace Application.Abstractions.Ingestion;

public sealed class StatementIngestionMessage
{
    public required Guid CustomerId { get; init; }

    public required string Period { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string DocumentId { get; init; }

    public string? Description { get; init; }

    public required Func<CancellationToken, Task<Stream>> OpenContentAsync { get; init; }

    public required string ReceiptHandle { get; init; }
}
