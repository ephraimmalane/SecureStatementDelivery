using Amazon;
using Amazon.SQS;
using Application.Abstractions.Ingestion;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    // Registers the SQS-backed pull ingestion source and its AWS client. Lives in Infrastructure so
    // the concrete source (and the SQS dependency) stay internal to this assembly; the presentation
    // layer only wires the worker + processor that consume the IStatementIngestionSource abstraction.
    // Call only when ingestion is enabled.
    public static IServiceCollection AddSqsIngestionSource(
        this IServiceCollection services,
        IngestionOptions options)
    {
        var sqsConfig = new AmazonSQSConfig();

        if (!string.IsNullOrWhiteSpace(options.Region))
        {
            sqsConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            sqsConfig.ServiceURL = options.ServiceUrl;
        }

        services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(sqsConfig));
        services.AddSingleton<IStatementIngestionSource, SqsStatementIngestionSource>();

        return services;
    }
}
