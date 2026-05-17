using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Statements.List;

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

        bool isAdmin = await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == requestingUserId && u.RoleId == Domain.Users.Role.Admin.Id, cancellationToken);

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
            dbQuery = dbQuery.Where(s => s.Period == query.Period);
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
                s.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedStatementResponse(items, totalCount, query.Page, query.PageSize);
    }
}
