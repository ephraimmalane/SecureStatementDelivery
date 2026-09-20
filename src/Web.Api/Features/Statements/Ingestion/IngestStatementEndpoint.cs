using Application.Abstractions.Messaging;
using Domain.Statements;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Features.Statements.Upload;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.Ingestion;

internal sealed class IngestStatementEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("statements/ingest", async (
            IFormFile file,
            [Microsoft.AspNetCore.Mvc.FromForm] Request request,
            ICommandHandler<UploadStatementCommand, Guid> handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { Error = "A non-empty PDF file is required." });
            }

            string? documentId = httpContext.Request.Headers["Document-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(documentId))
            {
                return Results.BadRequest(
                    new { Error = "A Document-Id header is required for ingestion." });
            }

            await using Stream fileStream = file.OpenReadStream();

            var command = new UploadStatementCommand(
                request.CustomerId,
                file.FileName,
                fileStream,
                file.ContentType,
                request.Period,
                request.Description ?? string.Empty,
                SystemPrincipals.StatementIngestionService,
                documentId);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                id => Results.Created($"/api/v1/statements/{id}", new { Id = id }),
                CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .RequireAuthorization(IngestionAuthorization.PolicyName)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data");
    }

    public sealed record Request(Guid CustomerId, string Period, string? Description);
}
