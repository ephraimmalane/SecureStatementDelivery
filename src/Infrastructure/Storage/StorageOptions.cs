namespace Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; init; } = "Local";

    public string LocalBasePath { get; init; } = "storage";

    // Single source of truth for the maximum accepted upload size.
    // Read by Kestrel (MaxRequestBodySize), the multipart form pipeline,
    // the upload validator, and the resumable (TUS) upload limit.
    public long MaxUploadBytes { get; init; } = 50L * 1024 * 1024;

    // Filesystem directory for in-progress resumable (TUS) uploads.
    // In multi-replica deployments this must be a shared RWX volume so that
    // chunks and resume/progress lookups work regardless of which pod is hit.
    public string ResumableUploadTempPath { get; init; } = "storage/uploads-temp";

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

    // Customer-managed KMS key id/ARN. When set, objects are encrypted at rest with
    // SSE-KMS using this key. When empty, uploads fall back to the bucket's default
    // encryption (configure SSE-KMS as the bucket default for production).
    public string KmsKeyId { get; init; } = string.Empty;

    // Write-once-read-many retention (S3 Object Lock). The bucket MUST be created with
    // Object Lock enabled for this to take effect. Statements become immutable and
    // undeletable until the retention period elapses (regulatory archival, e.g. SEC 17a-4).
    public bool UseObjectLock { get; init; }

    // "Governance" (privileged users can override) or "Compliance" (no one can override,
    // not even root, until retention elapses). Compliance is the stricter, audit-friendly mode.
    public string ObjectLockMode { get; init; } = "Governance";

    // Default ~7 years. Tune to the jurisdiction's statement retention requirement.
    public int RetentionDays { get; init; } = 2555;
}
