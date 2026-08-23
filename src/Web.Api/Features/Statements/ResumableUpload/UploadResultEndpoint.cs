using Application.Abstractions.Cache;
using Infrastructure.Authorization;
using Web.Api.Features;

namespace Web.Api.Features.Statements.ResumableUpload;

internal sealed class UploadResultEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements/upload/resumable/{fileId}/result", async (
            string fileId,
            ICacheService cache,
            CancellationToken cancellationToken) =>
        {
            ResumableUploadResult? result = await cache.GetAsync<ResumableUploadResult>(
                ResumableUploadResult.CacheKey(fileId),
                cancellationToken);

            if (result is null)
            {
                return Results.StatusCode(StatusCodes.Status202Accepted);
            }

            return result.Success
                ? Results.Ok(new { result.StatementId })
                : Results.BadRequest(new { result.Error });
        })
        .WithTags(Tags.Statements)
        .RequireAuthorization(Permissions.StatementsUpload)
        .RequireRateLimiting("api");
    }
}
