using Application.Abstractions.Messaging;

namespace Application.Statements.List;

public sealed record GetStatementsQuery(
    Guid? CustomerId,
    string? Period,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedStatementResponse>;

public sealed record PagedStatementResponse(
    IReadOnlyList<StatementSummaryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
