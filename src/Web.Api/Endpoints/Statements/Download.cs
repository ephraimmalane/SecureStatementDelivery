using Application.Abstractions.Messaging;
using Application.Statements.Download;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Statements;

internal sealed class Download : IEndpoint
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

            return response.RedirectUri is not null
                ? Results.Redirect(response.RedirectUri.AbsoluteUri, permanent: false)
                : Results.Stream(response.FileStream!, response.ContentType, response.FileName);
        })
        .WithTags(Tags.Statements)
        .AllowAnonymous();
    }
}
