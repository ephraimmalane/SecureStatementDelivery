using Application.Abstractions.Messaging;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Features.Statements.Download;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.DownloadInApp;

internal sealed class DownloadInAppEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements/{id:guid}/content", async (
            Guid id,
            HttpContext httpContext,
            IQueryHandler<DownloadInAppQuery, StatementFileResponse> handler,
            CancellationToken cancellationToken) =>
        {
            string? ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            string? userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var query = new DownloadInAppQuery(id, ipAddress, userAgent);
            Result<StatementFileResponse> result = await handler.Handle(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return CustomResults.Problem(result);
            }

            StatementFileResponse response = result.Value;

            return response.RedirectUri is not null
                ? Results.Redirect(response.RedirectUri.AbsoluteUri, permanent: false)
                : Results.Stream(
                    response.FileStream!,
                    response.ContentType,
                    response.FileName,
                    enableRangeProcessing: true);
        })
        .WithTags(Tags.Statements)
        // Authenticated: requires the caller's JWT + Statements.Download permission.
        .HasPermission(Permissions.StatementsDownload);
    }
}
