using Infrastructure.Ingestion;

namespace Web.Api.Features.Statements.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    // Wires the pull (object-storage event) ingestion path. Off unless Ingestion:Enabled is true, so
    // dev/CI never start the worker. When enabled it registers the SQS-backed source (from
    // Infrastructure), the per-message processor, and the background worker. Requires
    // Storage:Provider=S3 (the source streams the landed object via the shared IAmazonS3 client).
    public static IServiceCollection AddStatementIngestion(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));

        IngestionOptions options = configuration
            .GetSection(IngestionOptions.SectionName)
            .Get<IngestionOptions>() ?? new IngestionOptions();

        if (!options.Enabled)
        {
            return services;
        }

        services.AddSqsIngestionSource(options);
        services.AddScoped<StatementIngestionProcessor>();
        services.AddHostedService<StatementIngestionWorker>();

        return services;
    }
}
