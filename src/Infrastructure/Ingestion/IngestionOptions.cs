namespace Infrastructure.Ingestion;

// Configuration for the pull-based (object-storage event) ingestion worker. Disabled by default so
// local/dev/CI runs never start it; enable it in the environment (production) where the bank's
// statement generator drops rendered PDFs into a landing bucket wired to an SQS queue. Requires
// Storage:Provider=S3 (the worker fetches the landed object via the shared IAmazonS3 client).
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public bool Enabled { get; init; }

    // SQS queue subscribed to the landing bucket's s3:ObjectCreated:* notifications.
    public string QueueUrl { get; init; } = string.Empty;

    // SQS receive tuning. WaitTimeSeconds enables long polling (fewer empty round-trips).
    public int MaxMessagesPerPoll { get; init; } = 10;
    public int WaitTimeSeconds { get; init; } = 20;

    // Delay after an empty poll before polling again, and back-off after a poll error.
    public int EmptyPollDelaySeconds { get; init; } = 5;
    public int ErrorBackoffSeconds { get; init; } = 15;

    // Empty lets the AWS SDK resolve region from its default chain (env/IRSA); set for LocalStack.
    public string Region { get; init; } = string.Empty;
    public string? ServiceUrl { get; init; }
}
