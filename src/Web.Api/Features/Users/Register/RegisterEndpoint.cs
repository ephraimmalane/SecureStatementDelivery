using Application.Abstractions.Messaging;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Users.Register;

internal sealed class RegisterEndpoint : IEndpoint
{
    public sealed record Request(
        string Email,
        string FirstName,
        string LastName,
        string Password,
        string SouthAfricanIdNumber)
    {
#pragma warning disable S2068
        public override string ToString() =>
            $"Request {{ Email = {Email}, FirstName = {FirstName}, LastName = {LastName}, " +
            $"Password = [REDACTED], SouthAfricanIdNumber = [REDACTED] }}";
#pragma warning restore S2068
    }

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapAuthGroup();

        group.MapPost("register", async (
            Request request,
            ICommandHandler<RegisterUserCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RegisterUserCommand(
                request.Email,
                request.FirstName,
                request.LastName,
                request.Password,
                request.SouthAfricanIdNumber);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                userId => Results.Created($"/api/v1/users/{userId}", new { Id = userId }),
                CustomResults.Problem);
        });
    }
}
