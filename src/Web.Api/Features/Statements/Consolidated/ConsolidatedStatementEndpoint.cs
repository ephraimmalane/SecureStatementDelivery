using Application.Abstractions.Messaging;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Features.Statements.Download;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.Consolidated;

// "View 1 / 2 / 3 months" — the client maps each button to a from/to range (e.g. last 3 months:
// from = current-2, to = current). Always streams a freshly merged PDF (never a presigned redirect,
// since it is a generated document).
internal sealed class ConsolidatedStatementEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements/consolidated", async (
            string from,
            string to,
            Guid? customerId,
            HttpContext httpContext,
            IQueryHandler<ConsolidatedStatementQuery, StatementFileResponse> handler,
            CancellationToken cancellationToken) =>
        {
            string? ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            string? userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var query = new ConsolidatedStatementQuery(from, to, customerId, ipAddress, userAgent);
            Result<StatementFileResponse> result = await handler.Handle(query, cancellationToken);

            if (!result.IsSuccess)
            {
                return CustomResults.Problem(result);
            }

            StatementFileResponse response = result.Value;

            return Results.Stream(
                response.FileStream!,
                response.ContentType,
                response.FileName,
                enableRangeProcessing: true);
        })
        .WithTags(Tags.Statements)
        // Authenticated: caller's JWT + Statements.Download; ownership enforced in the handler.
        .HasPermission(Permissions.StatementsDownload);
    }
}
