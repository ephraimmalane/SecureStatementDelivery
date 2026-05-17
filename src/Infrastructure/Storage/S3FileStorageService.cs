using Amazon.S3;
using Amazon.S3.Model;
using Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Storage;

internal sealed class S3FileStorageService(
    IAmazonS3 s3Client,
    IOptions<StorageOptions> options,
    ILogger<S3FileStorageService> logger) : IFileStorageService
{
    private readonly string _bucket = options.Value.S3.BucketName;
    private readonly int _presignedExpiryMinutes = options.Value.S3.PresignedUrlExpiryMinutes;

    public async Task<StoredFile> StoreAsync(
        string fileName,
        Stream content,
        string contentType,
        string directory,
        CancellationToken cancellationToken)
    {
        string sanitizedName = SanitizeFileName(fileName);
        string key = $"{directory}/{Guid.NewGuid():N}_{sanitizedName}";
        long fileSize = content.CanSeek ? content.Length : 0L;

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        };

        await s3Client.PutObjectAsync(request, cancellationToken);

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

    private static string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
        return safe.Length > 100 ? safe[^100..] : safe;
    }
}
