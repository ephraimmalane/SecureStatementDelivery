using Application.Abstractions.Messaging;
using Application.Statements.List;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Statements;

internal sealed class List : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements", async (
            Guid? customerId,
            string? period,
            int page,
            int pageSize,
            IQueryHandler<GetStatementsQuery, PagedStatementResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetStatementsQuery(
                customerId,
                period,
                page <= 0 ? 1 : page,
                pageSize is <= 0 or > 100 ? 20 : pageSize);

            Result<PagedStatementResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .HasPermission(Permissions.StatementsReadOwn);
    }
}
