namespace Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; init; } = "Local";

    public string LocalBasePath { get; init; } = "storage";

    public S3StorageOptions S3 { get; init; } = new();
}

public sealed class S3StorageOptions
{
    public string BucketName { get; init; } = string.Empty;

    // Empty string lets the AWS SDK resolve the region from its default chain
    // (AWS_DEFAULT_REGION env var, ~/.aws/config, or IRSA metadata).
    public string Region { get; init; } = string.Empty;

    // Set to http://localhost:4566 when testing against LocalStack.
    public string? ServiceUrl { get; init; }

    // Required for LocalStack; not needed for real AWS.
    public bool ForcePathStyle { get; init; }

    public int PresignedUrlExpiryMinutes { get; init; } = 5;
}
