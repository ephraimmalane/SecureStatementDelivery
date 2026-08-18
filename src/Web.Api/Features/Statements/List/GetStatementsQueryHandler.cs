using System.Globalization;
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

        // Resolve the effective inclusive [from, to] month window. A preset Range is computed
        // server-side (last N completed months, ending with the previous month); otherwise the
        // caller's PeriodFrom/PeriodTo are used (Custom or unspecified). Because stored periods are
        // canonical YYYY-MM, a lexical string comparison is a valid chronological range, and each
        // bound is validated so a malformed filter fails fast rather than returning a confusing page.
        (string? from, string? to) = ResolvePeriodWindow(query);

        // string.Compare here is translated by EF to a server-side SQL comparison; the
        // StringComparison.Ordinal overload CA1309 recommends is not translatable, and the stored
        // values are ASCII YYYY-MM so ordinal and default ordering coincide anyway.
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

        return Result.Success(new PagedStatementResponse(items, totalCount, query.Page, query.PageSize));
    }

    // Presets resolve to the last N completed calendar months, ending with the previous month (the
    // current, incomplete month is excluded — use Custom for any other range, including a single
    // month via equal bounds). Anchored to UTC now. Computed bounds are always valid YYYY-MM.
    private static (string? From, string? To) ResolvePeriodWindow(GetStatementsQuery query)
    {
        DateTime now = DateTime.UtcNow;
        string MonthsAgo(int n) => now.AddMonths(-n).ToString("yyyy-MM", CultureInfo.InvariantCulture);

        return query.Range switch
        {
            StatementPeriodRange.LastMonth => (MonthsAgo(1), MonthsAgo(1)),
            StatementPeriodRange.Last3Months => (MonthsAgo(3), MonthsAgo(1)),
            StatementPeriodRange.Last6Months => (MonthsAgo(6), MonthsAgo(1)),
            StatementPeriodRange.Last12Months => (MonthsAgo(12), MonthsAgo(1)),
            _ => (query.PeriodFrom?.Trim(), query.PeriodTo?.Trim()), // Custom or unspecified
        };
    }
}
