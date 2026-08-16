using Application.Abstractions.Messaging;

namespace Web.Api.Features.Statements.GetById;

public sealed record GetStatementByIdQuery(Guid StatementId) : IQuery<StatementDetailResponse>;

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
    bool IsPasswordProtected,
    string? PasswordHint,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    string? RevokedReason);
