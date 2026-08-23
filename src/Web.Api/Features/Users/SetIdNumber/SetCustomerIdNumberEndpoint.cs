using Application.Abstractions.Messaging;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Users.SetIdNumber;

internal sealed class SetCustomerIdNumberEndpoint : IEndpoint
{
    public sealed record Request(string SouthAfricanIdNumber)
    {
#pragma warning disable S2068
        public override string ToString() => "Request { SouthAfricanIdNumber = [REDACTED] }";
#pragma warning restore S2068
    }

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("customers/{id:guid}/south-african-id", async (
            Guid id,
            [Microsoft.AspNetCore.Mvc.FromBody] Request request,
            ICommandHandler<SetCustomerIdNumberCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SetCustomerIdNumberCommand(id, request.SouthAfricanIdNumber);
            Result result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Users)
        .HasPermission(Permissions.AdminUsers);
    }
}
