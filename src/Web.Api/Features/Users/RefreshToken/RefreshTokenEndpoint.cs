using Application.Abstractions.Messaging;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Features.Users.Login;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Users.RefreshToken;

internal sealed class RefreshTokenEndpoint : IEndpoint
{
    public sealed record Request(string RefreshToken);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapAuthGroup();

        group.MapPost("refresh", async (
            Request request,
            ICommandHandler<RefreshTokenCommand, LoginResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RefreshTokenCommand(request.RefreshToken);
            Result<LoginResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }
}
