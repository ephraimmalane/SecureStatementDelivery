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

// Drains the transactional outbox: rehydrates each persisted domain event and dispatches it to
// its handlers, then marks it processed. Runs on every replica — FOR UPDATE SKIP LOCKED ensures
// each message is claimed by exactly one worker. This is also the natural seam to later publish
// to Kafka/Event Hubs instead of dispatching in-process.
internal sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    StatementMetrics metrics,
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
                // Never let a batch failure tear down the background service; the next tick retries.
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

            // Claim a batch of unprocessed messages, skipping rows other replicas hold.
            // BatchSize is interpolated as a parameter (not concatenated) so this is injection-safe.
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
                    message.ProcessedOnUtc = DateTime.UtcNow;
                    metrics.OutboxProcessed();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Mark processed-with-error so one poison message can't wedge the queue.
                    message.ProcessedOnUtc = DateTime.UtcNow;
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
