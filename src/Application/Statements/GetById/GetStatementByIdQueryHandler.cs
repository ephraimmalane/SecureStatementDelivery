using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Statements;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Statements.GetById;

internal sealed class GetStatementByIdQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext) : IQueryHandler<GetStatementByIdQuery, StatementDetailResponse>
{
    public async Task<Result<StatementDetailResponse>> Handle(
        GetStatementByIdQuery query,
        CancellationToken cancellationToken)
    {
        Guid requestingUserId = userContext.UserId;

        Statement? statement = await context.Statements
            .AsNoTracking()
            .Include(s => s.Customer)
            .SingleOrDefaultAsync(s => s.Id == query.StatementId, cancellationToken);

        if (statement is null)
        {
            return Result.Failure<StatementDetailResponse>(StatementErrors.NotFound(query.StatementId));
        }

        bool isAdmin = await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == requestingUserId && u.RoleId == Role.Admin.Id, cancellationToken);

        if (!isAdmin && statement.CustomerId != requestingUserId)
        {
            return Result.Failure<StatementDetailResponse>(StatementErrors.AccessDenied);
        }

        return new StatementDetailResponse(
            statement.Id,
            statement.CustomerId,
            statement.Customer.FullName,
            statement.OriginalFileName,
            statement.FileSizeBytes,
            statement.ContentType,
            statement.Period,
            statement.Description,
            statement.Status.ToString(),
            statement.CreatedAt,
            statement.RevokedAt,
            statement.RevokedReason);
    }
}
