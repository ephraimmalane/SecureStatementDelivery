namespace Application.Statements.GetById;

public sealed record StatementDetailResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string OriginalFileName,
    long FileSizeBytes,
    string ContentType,
    string Period,
    string Description,
    string Status,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    string? RevokedReason);
