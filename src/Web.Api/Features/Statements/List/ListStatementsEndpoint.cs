using Application.Abstractions.Messaging;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.List;

internal sealed class ListStatementsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements", async (
            Guid? customerId,
            string? period,
            string? periodFrom,
            string? periodTo,
            int page,
            int pageSize,
            IQueryHandler<GetStatementsQuery, PagedStatementResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetStatementsQuery(
                customerId,
                period,
                periodFrom,
                periodTo,
                page <= 0 ? 1 : page,
                pageSize is <= 0 or > 100 ? 20 : pageSize);

            Result<PagedStatementResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .HasPermission(Permissions.StatementsReadOwn);
    }
}
