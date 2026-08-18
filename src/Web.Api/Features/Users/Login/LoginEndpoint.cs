using Application.Abstractions.Messaging;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Users.Login;

internal sealed class LoginEndpoint : IEndpoint
{
    public sealed record Request(string Email, string Password)
    {
#pragma warning disable S2068
        public override string ToString() => $"Request {{ Email = {Email}, Password = [REDACTED] }}";
#pragma warning restore S2068
    }

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapAuthGroup();

        group.MapPost("login", async (
            Request request,
            ICommandHandler<LoginUserCommand, LoginResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new LoginUserCommand(request.Email, request.Password);
            Result<LoginResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }
}
