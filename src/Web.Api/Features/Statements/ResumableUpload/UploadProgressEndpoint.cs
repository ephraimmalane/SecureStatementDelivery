using System.Globalization;
using Infrastructure.Authorization;
using tusdotnet.Stores;
using Web.Api.Features;

namespace Web.Api.Features.Statements.ResumableUpload;

// Server-Sent Events stream of upload progress for a given TUS file id. Works across pods
// because it reads the offset/length from the shared TUS store rather than per-pod state.
// The client opens this alongside the upload and receives periodic { uploaded, total, percent }
// events until the upload completes (temp file removed) or the connection is closed.
internal sealed class UploadProgressEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("statements/upload/resumable/{fileId}/progress", async (
            string fileId,
            HttpContext httpContext,
            TusStorageProvider tus,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            // Disable Nginx response buffering so events are flushed to the client immediately.
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            TusDiskStore store = tus.Store;

            while (!cancellationToken.IsCancellationRequested)
            {
                bool exists = await store.FileExistAsync(fileId, cancellationToken);
                if (!exists)
                {
                    // The temp file is removed once finalisation completes (or it never existed).
                    await WriteEventAsync(httpContext, """{"status":"completed"}""", cancellationToken);
                    break;
                }

                long offset = await store.GetUploadOffsetAsync(fileId, cancellationToken);
                long? length = await store.GetUploadLengthAsync(fileId, cancellationToken);
                int percent = length is > 0 ? (int)(offset * 100 / length.Value) : 0;

                string totalJson = length?.ToString(CultureInfo.InvariantCulture) ?? "null";
                string offsetJson = offset.ToString(CultureInfo.InvariantCulture);
                string percentJson = percent.ToString(CultureInfo.InvariantCulture);
                string payload = $$"""{"uploaded":{{offsetJson}},"total":{{totalJson}},"percent":{{percentJson}}}""";
                await WriteEventAsync(httpContext, payload, cancellationToken);

                if (length.HasValue && offset >= length.Value)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        })
        .WithTags(Tags.Statements)
        .RequireAuthorization(Permissions.StatementsUpload);
    }

    private static async Task WriteEventAsync(HttpContext httpContext, string data, CancellationToken cancellationToken)
    {
        await httpContext.Response.WriteAsync($"data: {data}\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }
}
