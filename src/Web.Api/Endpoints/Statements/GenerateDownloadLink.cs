using Application.Abstractions.Messaging;
using Application.Statements.GenerateDownloadLink;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Statements;

internal sealed class GenerateDownloadLink : IEndpoint
{
    public sealed record Request(bool IsSingleUse = true, int ExpiryMinutes = 15);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("statements/{id:guid}/download-tokens", async (
            Guid id,
            Request? request,
            HttpContext httpContext,
            ICommandHandler<GenerateDownloadLinkCommand, DownloadLinkResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Request req = request ?? new Request();

            string? ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

            var command = new GenerateDownloadLinkCommand(
                id,
                req.IsSingleUse,
                req.ExpiryMinutes,
                ipAddress);

            Result<DownloadLinkResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .HasPermission(Permissions.StatementsDownload);
    }
}
