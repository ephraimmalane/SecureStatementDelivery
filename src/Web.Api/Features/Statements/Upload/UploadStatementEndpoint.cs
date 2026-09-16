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
        app.MapPost("customers/{customerId:guid}/statements", async (
            Guid customerId,
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

            string? documentId = httpContext.Request.Headers["Document-Id"].FirstOrDefault();

            var command = new UploadStatementCommand(
                customerId,
                file.FileName,
                fileStream,
                file.ContentType,
                request.Period ?? string.Empty,
                request.Description ?? string.Empty,
                userContext.UserId,
                documentId);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                id => Results.Created($"/statements/{id}", new { Id = id }),
                CustomResults.Problem);
        })
        .WithTags(Tags.Statements)
        .HasPermission(Permissions.StatementsUpload)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data");
    }

    public sealed record Request(string? Period, string? Description);
}
