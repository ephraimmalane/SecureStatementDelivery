using Application.Abstractions.Ingestion;
using Infrastructure.Ingestion;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Web.Api.Features.Statements.Ingestion;

// Background worker for the pull (object-storage event) ingestion path. Long-polls the source for
// statements produced by the bank's generation pipeline, feeds each through the funnel in its own DI
// scope, and acknowledges only after the statement is durably created. A failed message is left
// unacknowledged so the queue redelivers and ultimately dead-letters it — safe because the funnel
// deduplicates on the DocumentId (per customer), so a redelivered-after-success message is a no-op.
internal sealed class StatementIngestionWorker(
    IStatementIngestionSource source,
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<StatementIngestionWorker> logger) : BackgroundService
{
    private readonly IngestionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Statement ingestion worker started (queue {QueueUrl}).", _options.QueueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<StatementIngestionMessage> messages = await source.ReceiveAsync(stoppingToken);

                if (messages.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.EmptyPollDelaySeconds), stoppingToken);
                    continue;
                }

                foreach (StatementIngestionMessage message in messages)
                {
                    await ProcessOneAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The poll loop must survive any transient fault (SQS/S3 blip) and retry.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Statement ingestion poll failed; backing off.");
                await Task.Delay(TimeSpan.FromSeconds(_options.ErrorBackoffSeconds), stoppingToken);
            }
        }

        logger.LogInformation("Statement ingestion worker stopping.");
    }

    private async Task ProcessOneAsync(StatementIngestionMessage message, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        StatementIngestionProcessor processor =
            scope.ServiceProvider.GetRequiredService<StatementIngestionProcessor>();

        Result<Guid> result = await processor.ProcessAsync(message, cancellationToken);

        if (result.IsSuccess)
        {
            await source.AcknowledgeAsync(message, cancellationToken);
            logger.LogInformation(
                "Ingested statement {StatementId} for customer {CustomerId} (document {DocumentId}).",
                result.Value, message.CustomerId, message.DocumentId);
        }
        else
        {
            // Not acknowledged — the queue redelivers, then dead-letters after maxReceiveCount.
            logger.LogWarning(
                "Ingestion rejected for customer {CustomerId} (document {DocumentId}): {Error}. Left for redelivery.",
                message.CustomerId, message.DocumentId, result.Error.Code);
        }
    }
}
