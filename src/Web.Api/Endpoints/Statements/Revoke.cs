using Application.Abstractions.Messaging;
using Application.Statements.Revoke;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Statements;

internal sealed class Revoke : IEndpoint
{
    public sealed record Request(string Reason);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("statements/{id:guid}", async (
            Guid id,
            [Microsoft.AspNetCore.Mvc.FromBody] Request request,
            ICommandHandler<RevokeStatementCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RevokeStatementCommand(id, request.Reason);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .HasPermission(Permissions.StatementsRevoke);
    }
}
