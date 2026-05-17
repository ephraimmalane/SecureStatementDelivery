using Application.Abstractions.Messaging;
using Application.Statements.Upload;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Statements;

internal sealed class Upload : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("statements/upload", async (
            IFormFile file,
            [Microsoft.AspNetCore.Mvc.FromForm] UploadRequest request,
            ICommandHandler<UploadStatementCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { Error = "A PDF file is required." });
            }

            await using Stream fileStream = file.OpenReadStream();

            var command = new UploadStatementCommand(
                request.CustomerId,
                file.FileName,
                fileStream,
                file.ContentType,
                request.Period,
                request.Description ?? string.Empty);

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

    public sealed record UploadRequest(
        Guid CustomerId,
        string Period,
        string? Description);
}
