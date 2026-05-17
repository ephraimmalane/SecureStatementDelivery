using Application.Abstractions.Messaging;
using Application.Statements.GetById;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Statements;

internal sealed class GetById : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements/{id:guid}", async (
            Guid id,
            IQueryHandler<GetStatementByIdQuery, StatementDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetStatementByIdQuery(id);
            Result<StatementDetailResponse> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .HasPermission(Permissions.StatementsReadOwn);
    }
}
