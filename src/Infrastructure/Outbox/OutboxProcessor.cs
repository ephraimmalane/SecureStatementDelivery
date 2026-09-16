using System.Text.Json;
using Application.Abstractions.Observability;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
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
    IStatementMetrics metrics,
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

            DateTime now = timeProvider.GetUtcNow().UtcDateTime;

            List<OutboxMessage> messages = await dbContext.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT * FROM public.outbox_messages
                    WHERE processed_on_utc IS NULL
                      AND (next_attempt_utc IS NULL OR next_attempt_utc <= {now})
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
                    message.Error = null;
                    metrics.OutboxProcessed();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    bool deadLettered = RecordFailure(
                        message, ex.ToString(), timeProvider.GetUtcNow().UtcDateTime, _options);
                    metrics.OutboxFailed();

                    if (deadLettered)
                    {
                        logger.LogError(ex,
                            "Outbox message {MessageId} dead-lettered after {RetryCount} attempts.",
                            message.Id, message.RetryCount);
                    }
                    else
                    {
                        logger.LogWarning(ex,
                            "Outbox message {MessageId} failed on attempt {RetryCount}; next attempt at {NextAttemptUtc:o}.",
                            message.Id, message.RetryCount, message.NextAttemptUtc);
                    }
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

    internal static bool RecordFailure(OutboxMessage message, string error, DateTime nowUtc, OutboxOptions options)
    {
        message.RetryCount++;
        message.Error = error;

        if (message.RetryCount >= options.MaxRetries)
        {
            message.ProcessedOnUtc = nowUtc;
            message.NextAttemptUtc = null;
            return true;
        }

        double delaySeconds = Math.Min(
            options.BaseRetryDelaySeconds * Math.Pow(2, message.RetryCount - 1),
            options.MaxRetryDelaySeconds);

        message.NextAttemptUtc = nowUtc.AddSeconds(delaySeconds);
        return false;
    }
}
