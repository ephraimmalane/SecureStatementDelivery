using System.Text;
using Domain.Statements;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Stores;

namespace Web.Api.Features.Statements.ResumableUpload;

// Singleton. Holds the shared TUS disk store and the configuration the TUS middleware
// uses for every request. The store directory must live on shared (RWX) storage in
// multi-replica deployments so chunks, resume, and progress lookups work on any pod.
internal sealed class TusStorageProvider
{
    public TusDiskStore Store { get; }

    public DefaultTusConfiguration Configuration { get; }

    public TusStorageProvider(IOptions<StorageOptions> options)
    {
        string tempPath = Path.GetFullPath(options.Value.ResumableUploadTempPath);
        Directory.CreateDirectory(tempPath);

        Store = new TusDiskStore(tempPath);

        Configuration = new DefaultTusConfiguration
        {
            Store = Store,
            // Use the long variant so the limit supports files larger than 2 GB.
            MaxAllowedUploadSizeInBytesLong = options.Value.MaxUploadBytes,
            Events = new Events
            {
                // Reject the upload up front if required metadata is absent — cheaper than
                // discovering it only after the whole file has been transferred.
                OnBeforeCreateAsync = ctx =>
                {
                    if (!ctx.Metadata.ContainsKey("customerId"))
                    {
                        ctx.FailRequest("A 'customerId' metadata value is required.");
                    }

                    if (!ctx.Metadata.TryGetValue("period", out Metadata? periodMeta))
                    {
                        ctx.FailRequest("A 'period' metadata value is required.");
                    }
                    else if (!Statement.IsValidPeriod(periodMeta.GetString(Encoding.UTF8)))
                    {
                        // Reject up front, before any chunk is transferred, rather than after
                        // the whole file lands and finalisation fails the domain invariant.
                        ctx.FailRequest("The 'period' metadata must be in YYYY-MM format, e.g. 2024-01.");
                    }

                    return Task.CompletedTask;
                },

                // Finalisation runs in the authenticated request scope of the final chunk.
                OnFileCompleteAsync = async ctx =>
                {
                    ResumableUploadCompletedHandler handler = ctx.HttpContext.RequestServices
                        .GetRequiredService<ResumableUploadCompletedHandler>();

                    await handler.HandleAsync(ctx);
                }
            }
        };
    }
}
