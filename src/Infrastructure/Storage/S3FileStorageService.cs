using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Storage;

internal sealed class S3FileStorageService(
    IAmazonS3 s3Client,
    IOptions<StorageOptions> options,
    ILogger<S3FileStorageService> logger) : IFileStorageService, IDisposable
{
    private readonly S3StorageOptions _s3 = options.Value.S3;
    private readonly string _bucket = options.Value.S3.BucketName;
    private readonly int _presignedExpiryMinutes = options.Value.S3.PresignedUrlExpiryMinutes;

    private readonly TransferUtility _transfer = new(s3Client);

    public async Task<StoredFile> StoreAsync(
        string fileName,
        Stream content,
        string contentType,
        string directory,
        CancellationToken cancellationToken)
    {
        string sanitizedName = SanitizeFileName(fileName);
        string key = $"{directory}/{Guid.NewGuid():N}_{sanitizedName}";
        GuardAgainstTraversal(key, directory);
        long fileSize = content.CanSeek ? content.Length : 0L;

        var request = new TransferUtilityUploadRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            PartSize = 8L * 1024 * 1024
        };

        if (!string.IsNullOrWhiteSpace(_s3.KmsKeyId))
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;
            request.ServerSideEncryptionKeyManagementServiceKeyId = _s3.KmsKeyId;
        }

        if (_s3.UseObjectLock)
        {
            request.ObjectLockMode = _s3.ObjectLockMode.Equals("Compliance", StringComparison.OrdinalIgnoreCase)
                ? ObjectLockMode.Compliance
                : ObjectLockMode.Governance;
            request.ObjectLockRetainUntilDate = DateTime.UtcNow.AddDays(_s3.RetentionDays);
        }

        await _transfer.UploadAsync(request, cancellationToken);

        logger.LogDebug("Stored S3 object {Key} in bucket {Bucket}", key, _bucket);

        return new StoredFile(key, fileSize);
    }

    public async Task<Stream> RetrieveAsync(string storagePath, CancellationToken cancellationToken)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucket,
            Key = storagePath
        };

        GetObjectResponse response = await s3Client.GetObjectAsync(request, cancellationToken);

        return response.ResponseStream;
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = storagePath
        };

        await s3Client.DeleteObjectAsync(request, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucket,
                Key = storagePath
            };

            await s3Client.GetObjectMetadataAsync(request, cancellationToken);

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<Uri?> GeneratePresignedDownloadUriAsync(
        string storagePath,
        TimeSpan expiry,
        CancellationToken cancellationToken)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = storagePath,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(_presignedExpiryMinutes)
        };

        string rawUrl = await s3Client.GetPreSignedURLAsync(request);

        return new Uri(rawUrl);
    }

    public void Dispose() => _transfer.Dispose();

    private static string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
        return safe.Length > 100 ? safe[^100..] : safe;
    }

    private static void GuardAgainstTraversal(string key, string directory)
    {
        bool hasTraversalSegment = key
            .Split('/')
            .Any(segment => segment is ".." or ".");

        if (hasTraversalSegment || !key.StartsWith($"{directory}/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path traversal detected. Access denied.");
        }
    }
}
