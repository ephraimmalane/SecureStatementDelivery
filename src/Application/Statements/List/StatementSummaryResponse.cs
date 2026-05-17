namespace Application.Statements.List;

public sealed record StatementSummaryResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string OriginalFileName,
    long FileSizeBytes,
    string Period,
    string Description,
    string Status,
    DateTime CreatedAt);
