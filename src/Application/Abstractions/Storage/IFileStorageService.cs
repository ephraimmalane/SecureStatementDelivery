namespace Application.Abstractions.Storage;

public sealed record StoredFile(string StoragePath, long FileSizeBytes);

public interface IFileStorageService
{
    Task<StoredFile> StoreAsync(
        string fileName,
        Stream content,
        string contentType,
        string directory,
        CancellationToken cancellationToken);

    Task<Stream> RetrieveAsync(string storagePath, CancellationToken cancellationToken);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken);

    // Returns null for providers that do not support pre-signed URLs (e.g. Local).
    Task<Uri?> GeneratePresignedDownloadUriAsync(
        string storagePath,
        TimeSpan expiry,
        CancellationToken cancellationToken);
}
