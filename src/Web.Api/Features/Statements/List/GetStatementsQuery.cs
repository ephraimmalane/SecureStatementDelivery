using Application.Abstractions.Messaging;

namespace Web.Api.Features.Statements.List;

public sealed record GetStatementsQuery(
    Guid? CustomerId,
    StatementPeriodRange? Range,
    string? PeriodFrom,
    string? PeriodTo,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedStatementResponse>;

public sealed record PagedStatementResponse(
    IReadOnlyList<StatementSummaryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record StatementSummaryResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string OriginalFileName,
    long FileSizeBytes,
    string Period,
    string Description,
    string Status,
    bool IsPasswordProtected,
    string? PasswordHint,
    DateTime CreatedAt);
