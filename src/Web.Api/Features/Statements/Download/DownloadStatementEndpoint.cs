using Application.Abstractions.Messaging;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.Download;

internal sealed class DownloadStatementEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements/download", async (
            string token,
            HttpContext httpContext,
            IQueryHandler<DownloadStatementQuery, StatementFileResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.BadRequest(new { Error = "A download token is required." });
            }

            string? ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            string? userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var query = new DownloadStatementQuery(token, ipAddress, userAgent);
            Result<StatementFileResponse> result = await handler.Handle(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return CustomResults.Problem(result);
            }

            StatementFileResponse response = result.Value;

            // S3 path: presigned URL redirect — S3 handles range requests and parallel chunked
            // downloads natively. The API transfers zero bytes of the file payload.
            //
            // Local path: enableRangeProcessing=true makes ASP.NET Core emit Accept-Ranges: bytes
            // and honour Range: headers so browsers and download managers can resume interrupted
            // downloads. Requires the underlying stream to be seekable (File.OpenRead is seekable).
            return response.RedirectUri is not null
                ? Results.Redirect(response.RedirectUri.AbsoluteUri, permanent: false)
                : Results.Stream(
                    response.FileStream!,
                    response.ContentType,
                    response.FileName,
                    enableRangeProcessing: true);
        })
        .WithTags(Tags.Statements)
        .AllowAnonymous()
        .RequireRateLimiting("api");
    }
}
