using Amazon;
using Amazon.SQS;
using Application.Abstractions.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Infrastructure.Notifications;

public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));

        NotificationOptions options = configuration
            .GetSection(NotificationOptions.SectionName)
            .Get<NotificationOptions>() ?? new NotificationOptions();

        if (string.IsNullOrWhiteSpace(options.QueueUrl))
        {
            services.AddSingleton<INotificationService, LoggingNotificationService>();
            return services;
        }

        var sqsConfig = new AmazonSQSConfig();

        if (!string.IsNullOrWhiteSpace(options.Region))
        {
            sqsConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            sqsConfig.ServiceURL = options.ServiceUrl;
        }

        services.TryAddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(sqsConfig));
        services.AddSingleton<INotificationService, SqsNotificationService>();

        return services;
    }
}
