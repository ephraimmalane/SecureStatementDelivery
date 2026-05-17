using Application.Abstractions.Messaging;
using Application.Users.Login;
using Application.Users.RefreshToken;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class RefreshToken : IEndpoint
{
    public sealed record Request(string RefreshToken);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("auth/refresh", async (
            Request request,
            ICommandHandler<RefreshTokenCommand, LoginResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RefreshTokenCommand(request.RefreshToken);

            Result<LoginResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Auth)
        .AllowAnonymous();
    }
}
