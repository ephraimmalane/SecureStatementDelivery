using Application.Abstractions.Messaging;
using Domain.Statements;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Features.Statements.Upload;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.Ingestion;

// Machine-to-machine ingestion (push): the bank's statement-generation system posts a rendered PDF
// here, authenticated as a Keycloak service account (client_credentials grant) carrying the
// statement-ingest realm role. This is the production counterpart to the human admin upload — it
// feeds the exact same funnel (validate → scan → encrypt → store → Statement + audit), with no
// human actor. A Document-Id is mandatory because any real delivery pipeline is at-least-once.
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
                return Results.BadRequest(new { Error = "A PDF file is required." });
            }

            string? documentId = httpContext.Request.Headers["Document-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(documentId))
            {
                // Without a document id a redelivered (retried) push would create a duplicate statement.
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
                // No human uploaded this — attribute it to the ingestion service principal.
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

    // The statement is always AES-encrypted with the customer's South African ID number as the open
    // password (looked up server-side), so no password is accepted from the caller.
    public sealed record Request(Guid CustomerId, string Period, string? Description);
}
