using Application.Abstractions.Ingestion;
using Infrastructure.Ingestion;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Web.Api.Features.Statements.Ingestion;

internal sealed class StatementIngestionWorker(
    IStatementIngestionSource source,
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<StatementIngestionWorker> logger) : BackgroundService
{
    private readonly IngestionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Statement ingestion worker started (queue {QueueUrl}, max concurrency {MaxConcurrency}).",
            _options.QueueUrl, _options.MaxConcurrency);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _options.MaxConcurrency)
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<StatementIngestionMessage> messages;

            try
            {
                messages = await source.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Statement ingestion poll failed; backing off.");
                await Task.Delay(TimeSpan.FromSeconds(_options.ErrorBackoffSeconds), stoppingToken);
                continue;
            }

            if (messages.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.EmptyPollDelaySeconds), stoppingToken);
                continue;
            }

            await Parallel.ForEachAsync(messages, parallelOptions, ProcessOneAsync);
        }

        logger.LogInformation("Statement ingestion worker stopping.");
    }

    private async ValueTask ProcessOneAsync(
        StatementIngestionMessage message,
        CancellationToken cancellationToken)
    {
        try
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
                logger.LogWarning(
                    "Ingestion rejected for customer {CustomerId} (document {DocumentId}): {Error}. Left for redelivery.",
                    message.CustomerId, message.DocumentId, result.Error.Code);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(
                ex,
                "Ingestion threw for customer {CustomerId} (document {DocumentId}); left for redelivery.",
                message.CustomerId, message.DocumentId);
        }
    }
}
