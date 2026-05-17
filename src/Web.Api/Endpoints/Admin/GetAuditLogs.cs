using Application.Abstractions.Messaging;
using Application.Admin.AuditLogs;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

internal sealed class GetAuditLogs : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/audit-logs", async (
            Guid? statementId,
            Guid? userId,
            string? action,
            DateTime? from,
            DateTime? to,
            int page,
            int pageSize,
            IQueryHandler<GetAuditLogsQuery, PagedAuditLogResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetAuditLogsQuery(
                statementId,
                userId,
                action,
                from,
                to,
                page <= 0 ? 1 : page,
                pageSize is <= 0 or > 200 ? 50 : pageSize);

            Result<PagedAuditLogResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Admin)
        .HasPermission(Permissions.AdminAuditLogs);
    }
}
