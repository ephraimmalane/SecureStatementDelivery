using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Features;
using Web.Api.Infrastructure;

namespace Web.Api.Features.Statements.Upload;

internal sealed class UploadStatementEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("statements/upload", async (
            IFormFile file,
            [Microsoft.AspNetCore.Mvc.FromForm] Request request,
            ICommandHandler<UploadStatementCommand, Guid> handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { Error = "A PDF file is required." });
            }

            await using Stream fileStream = file.OpenReadStream();

            // Optional source-assigned document id: a redelivered upload carrying the same Document-Id
            // (for the same customer) is deduplicated.
            string? documentId = httpContext.Request.Headers["Document-Id"].FirstOrDefault();

            var command = new UploadStatementCommand(
                request.CustomerId,
                file.FileName,
                fileStream,
                file.ContentType,
                request.Period,
                request.Description ?? string.Empty,
                // The human admin performing the upload is the recorded actor.
                userContext.UserId,
                documentId);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                id => Results.Created($"/api/v1/statements/{id}", new { Id = id }),
                CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .HasPermission(Permissions.StatementsUpload)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data");
    }

    // The statement is always AES-encrypted with the customer's South African ID number as the open
    // password (looked up server-side), so no password is accepted from the caller.
    public sealed record Request(Guid CustomerId, string Period, string? Description);
}
