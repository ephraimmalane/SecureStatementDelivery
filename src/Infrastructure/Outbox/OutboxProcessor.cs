using System.Text.Json;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Outbox;

internal sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    StatementMetrics metrics,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox batch processing failed.");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IDomainEventsDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventsDispatcher>();

        IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            List<OutboxMessage> messages = await dbContext.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT * FROM public.outbox_messages
                    WHERE processed_on_utc IS NULL
                    ORDER BY occurred_on_utc
                    LIMIT {_options.BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            foreach (OutboxMessage message in messages)
            {
                try
                {
                    IDomainEvent domainEvent = Deserialize(message);
                    await dispatcher.DispatchAsync([domainEvent], cancellationToken);
                    message.ProcessedOnUtc = timeProvider.GetUtcNow().UtcDateTime;
                    metrics.OutboxProcessed();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    message.ProcessedOnUtc = timeProvider.GetUtcNow().UtcDateTime;
                    message.Error = ex.ToString();
                    metrics.OutboxFailed();
                    logger.LogError(ex, "Failed to process outbox message {MessageId}.", message.Id);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static IDomainEvent Deserialize(OutboxMessage message)
    {
        Type? type = Type.GetType(message.Type)
            ?? throw new InvalidOperationException(
                $"Cannot resolve outbox message type '{message.Type}'.");

        return (IDomainEvent)JsonSerializer.Deserialize(message.Content, type)!;
    }
}
