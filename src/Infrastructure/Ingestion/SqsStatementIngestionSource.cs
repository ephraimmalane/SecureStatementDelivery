using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Application.Abstractions.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Ingestion;

// Pull-based ingestion backed by S3 -> SQS: the landing bucket publishes s3:ObjectCreated:*
// notifications to an SQS queue; this source turns each notification into a StatementIngestionMessage.
// The statement's metadata travels as S3 object user-metadata (x-amz-meta-*) set by the generator
// when it writes the PDF, so the queue message stays a thin pointer and the bytes are streamed only
// when the processor asks for them.
internal sealed class SqsStatementIngestionSource(
    IAmazonSQS sqs,
    IAmazonS3 s3,
    IOptions<IngestionOptions> options,
    ILogger<SqsStatementIngestionSource> logger) : IStatementIngestionSource
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly IngestionOptions _options = options.Value;

    public async Task<IReadOnlyList<StatementIngestionMessage>> ReceiveAsync(
        CancellationToken cancellationToken)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _options.QueueUrl,
            MaxNumberOfMessages = _options.MaxMessagesPerPoll,
            WaitTimeSeconds = _options.WaitTimeSeconds
        };

        ReceiveMessageResponse response = await sqs.ReceiveMessageAsync(request, cancellationToken);

        var messages = new List<StatementIngestionMessage>();

        foreach (Message sqsMessage in response.Messages)
        {
            S3EventRecord? record = ParseFirstObjectRecord(sqsMessage.Body);

            if (record is null)
            {
                // Not a statement object-created event (e.g. the s3:TestEvent emitted when the
                // notification is first configured). Remove it so it doesn't recirculate.
                await sqs.DeleteMessageAsync(_options.QueueUrl, sqsMessage.ReceiptHandle, cancellationToken);
                continue;
            }

            string bucket = record.S3!.Bucket!.Name!;
            // Keys arrive URL-encoded (spaces as '+', unicode percent-encoded).
            string key = Uri.UnescapeDataString(record.S3.Object!.Key!.Replace("+", " ", StringComparison.Ordinal));

            StatementIngestionMessage? message = await TryBuildMessageAsync(
                bucket, key, sqsMessage.ReceiptHandle, cancellationToken);

            // A message that can't be built (missing/invalid metadata) is left un-deleted on
            // purpose: it becomes invisible until the visibility timeout, is redelivered, and after
            // the queue's maxReceiveCount is routed to the dead-letter queue for inspection.
            if (message is not null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    public Task AcknowledgeAsync(StatementIngestionMessage message, CancellationToken cancellationToken) =>
        sqs.DeleteMessageAsync(_options.QueueUrl, message.ReceiptHandle, cancellationToken);

    private async Task<StatementIngestionMessage?> TryBuildMessageAsync(
        string bucket,
        string key,
        string receiptHandle,
        CancellationToken cancellationToken)
    {
        GetObjectMetadataResponse metadata = await s3.GetObjectMetadataAsync(bucket, key, cancellationToken);

        string? customerIdRaw = metadata.Metadata["customerid"];
        string? period = metadata.Metadata["period"];
        string? documentId = metadata.Metadata["documentid"];

        if (!Guid.TryParse(customerIdRaw, out Guid customerId) ||
            string.IsNullOrWhiteSpace(period) ||
            string.IsNullOrWhiteSpace(documentId))
        {
            logger.LogError(
                "Ingestion object {Bucket}/{Key} is missing required metadata (customerid/period/documentid); leaving for dead-letter routing.",
                bucket, key);
            return null;
        }

        string fileName = metadata.Metadata["filename"] is { Length: > 0 } name
            ? name
            : Path.GetFileName(key);

        string contentType = string.IsNullOrWhiteSpace(metadata.Headers.ContentType)
            ? "application/pdf"
            : metadata.Headers.ContentType;

        return new StatementIngestionMessage
        {
            CustomerId = customerId,
            Period = period,
            FileName = fileName,
            ContentType = contentType,
            DocumentId = documentId,
            Description = metadata.Metadata["description"],
            ReceiptHandle = receiptHandle,
            OpenContentAsync = ct => OpenObjectAsync(bucket, key, ct)
        };
    }

    private async Task<Stream> OpenObjectAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        GetObjectResponse response = await s3.GetObjectAsync(bucket, key, cancellationToken);
        return response.ResponseStream;
    }

    private S3EventRecord? ParseFirstObjectRecord(string body)
    {
        try
        {
            S3EventNotification? notification = JsonSerializer.Deserialize<S3EventNotification>(body, JsonOptions);
            return notification?.Records?
                .FirstOrDefault(r => r.S3?.Bucket?.Name is not null && r.S3.Object?.Key is not null);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Skipping non-JSON SQS message on the ingestion queue.");
            return null;
        }
    }

    // Minimal projection of the S3 event notification envelope.
    private sealed record S3EventNotification([property: JsonPropertyName("Records")] List<S3EventRecord>? Records);

    private sealed record S3EventRecord([property: JsonPropertyName("s3")] S3Entity? S3);

    private sealed record S3Entity(
        [property: JsonPropertyName("bucket")] S3Bucket? Bucket,
        [property: JsonPropertyName("object")] S3Object? Object);

    private sealed record S3Bucket([property: JsonPropertyName("name")] string? Name);

    private sealed record S3Object([property: JsonPropertyName("key")] string? Key);
}
