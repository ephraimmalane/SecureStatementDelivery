namespace Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; init; } = "Local";

    public string LocalBasePath { get; init; } = "storage";

    public long MaxUploadBytes { get; init; } = 50L * 1024 * 1024;

    public string ResumableUploadTempPath { get; init; } = "storage/uploads-temp";

    public S3StorageOptions S3 { get; init; } = new();
}

public sealed class S3StorageOptions
{
    public string BucketName { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string? ServiceUrl { get; init; }

    public bool ForcePathStyle { get; init; }

    public int PresignedUrlExpiryMinutes { get; init; } = 5;

    public string KmsKeyId { get; init; } = string.Empty;

    public bool UseObjectLock { get; init; }

    public string ObjectLockMode { get; init; } = "Governance";

    public int RetentionDays { get; init; } = 2555;
}
