using Infrastructure.Ingestion;

namespace Web.Api.Features.Statements.Ingestion;

public static class IngestionServiceCollectionExtensions
{
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
