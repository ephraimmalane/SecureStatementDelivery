using Application.Abstractions.Messaging;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.GetById;

internal sealed class GetStatementByIdEndpoint : IEndpoint
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
