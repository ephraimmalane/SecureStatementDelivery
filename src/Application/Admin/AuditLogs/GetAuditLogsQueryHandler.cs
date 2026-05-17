using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.AuditLogs;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin.AuditLogs;

internal sealed class GetAuditLogsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAuditLogsQuery, PagedAuditLogResponse>
{
    public async Task<Result<PagedAuditLogResponse>> Handle(
        GetAuditLogsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<DownloadAuditLog> logQuery = context.DownloadAuditLogs.AsNoTracking();

        if (query.StatementId.HasValue)
        {
            logQuery = logQuery.Where(x => x.StatementId == query.StatementId.Value);
        }

        if (query.UserId.HasValue)
        {
            logQuery = logQuery.Where(x => x.UserId == query.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Action) &&
            Enum.TryParse<AuditAction>(query.Action, ignoreCase: true, out AuditAction parsedAction))
        {
            logQuery = logQuery.Where(x => x.Action == parsedAction);
        }

        if (query.From.HasValue)
        {
            logQuery = logQuery.Where(x => x.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            logQuery = logQuery.Where(x => x.OccurredAt <= query.To.Value);
        }

        int totalCount = await logQuery.CountAsync(cancellationToken);

        List<AuditLogResponse> items = await logQuery
            .OrderByDescending(l => l.OccurredAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Join(
                context.Users,
                log => log.UserId,
                user => user.Id,
                (log, user) => new AuditLogResponse(
                    log.Id,
                    log.StatementId,
                    log.UserId,
                    user.FirstName + " " + user.LastName,
                    log.DownloadTokenId,
                    log.Action.ToString(),
                    log.IpAddress,
                    log.UserAgent,
                    log.OccurredAt,
                    log.AdditionalData))
            .ToListAsync(cancellationToken);

        return new PagedAuditLogResponse(items, totalCount, query.Page, query.PageSize);
    }
}
