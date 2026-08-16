using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Features.Statements.List;

internal sealed class GetStatementsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext) : IQueryHandler<GetStatementsQuery, PagedStatementResponse>
{
    public async Task<Result<PagedStatementResponse>> Handle(
        GetStatementsQuery query,
        CancellationToken cancellationToken)
    {
        Guid requestingUserId = userContext.UserId;

        IQueryable<Statement> dbQuery = context.Statements
            .AsNoTracking()
            .Include(s => s.Customer)
            .AsQueryable();

        bool isAdmin = userContext.IsAdmin;

        if (!isAdmin)
        {
            dbQuery = dbQuery.Where(s => s.CustomerId == requestingUserId);
        }
        else if (query.CustomerId.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.CustomerId == query.CustomerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Period))
        {
            // Exact single-month match takes precedence. Trim so stray whitespace from a query
            // string can't turn a real match into an empty page. Stored periods are normalised to
            // canonical YYYY-MM on write.
            string periodFilter = query.Period.Trim();
            dbQuery = dbQuery.Where(s => s.Period == periodFilter);
        }
        else
        {
            // Inclusive [from, to] month range. Because stored periods are canonical YYYY-MM, a
            // lexical string comparison is a valid chronological range. Each bound is validated
            // against the same domain invariant so a malformed filter fails fast rather than
            // returning a confusing empty (or wrong) page.
            string? from = query.PeriodFrom?.Trim();
            string? to = query.PeriodTo?.Trim();

            // string.Compare here is translated by EF to a server-side SQL comparison; the
            // StringComparison.Ordinal overload CA1309 recommends is not translatable, and the
            // stored values are ASCII YYYY-MM so ordinal and default ordering coincide anyway.
#pragma warning disable CA1309
            if (!string.IsNullOrEmpty(from))
            {
                if (!Statement.IsValidPeriod(from))
                {
                    return Result.Failure<PagedStatementResponse>(StatementErrors.InvalidPeriodFormat);
                }

                dbQuery = dbQuery.Where(s => string.Compare(s.Period, from) >= 0);
            }

            if (!string.IsNullOrEmpty(to))
            {
                if (!Statement.IsValidPeriod(to))
                {
                    return Result.Failure<PagedStatementResponse>(StatementErrors.InvalidPeriodFormat);
                }

                dbQuery = dbQuery.Where(s => string.Compare(s.Period, to) <= 0);
            }
#pragma warning restore CA1309
        }

        int totalCount = await dbQuery.CountAsync(cancellationToken);

        List<StatementSummaryResponse> items = await dbQuery
            .OrderByDescending(s => s.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new StatementSummaryResponse(
                s.Id,
                s.CustomerId,
                s.Customer.FullName,
                s.OriginalFileName,
                s.FileSizeBytes,
                s.Period,
                s.Description,
                s.Status.ToString(),
                s.IsPasswordProtected,
                s.IsPasswordProtected ? StatementMessages.PasswordHint : null,
                s.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedStatementResponse(items, totalCount, query.Page, query.PageSize);
    }
}
