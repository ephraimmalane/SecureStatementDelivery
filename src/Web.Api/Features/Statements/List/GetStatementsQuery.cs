using Application.Abstractions.Messaging;

namespace Web.Api.Features.Statements.List;

// Period filtering: `Range` selects a preset window (last 1/3/6/12 completed months) resolved
// server-side, or `Custom` to use `PeriodFrom`/`PeriodTo` (inclusive, either bound optional; equal
// bounds give a single month). When `Range` is unspecified, any supplied PeriodFrom/PeriodTo are
// used directly. All periods are canonical YYYY-MM, which sorts lexically, so the range is a plain
// string comparison.
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
