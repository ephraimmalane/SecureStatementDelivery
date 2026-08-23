using System.Text;
using Domain.Statements;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Stores;

namespace Web.Api.Features.Statements.ResumableUpload;

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
            MaxAllowedUploadSizeInBytesLong = options.Value.MaxUploadBytes,
            Events = new Events
            {
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
                        ctx.FailRequest("The 'period' metadata must be in YYYY-MM format, e.g. 2024-01.");
                    }

                    return Task.CompletedTask;
                },

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
