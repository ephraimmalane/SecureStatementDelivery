using Application.Abstractions.Messaging;

namespace Web.Api.Features.Statements.List;

// Period filtering: `Period` is an exact single-month match and takes precedence; otherwise
// `PeriodFrom`/`PeriodTo` form an inclusive month range (either bound optional). All are canonical
// YYYY-MM, which sorts lexically, so the range is a plain string comparison.
public sealed record GetStatementsQuery(
    Guid? CustomerId,
    string? Period,
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
