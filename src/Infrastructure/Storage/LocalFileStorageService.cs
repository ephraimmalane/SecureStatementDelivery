using Application.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Storage;

internal sealed class LocalFileStorageService(
    IOptions<StorageOptions> options,
    ILogger<LocalFileStorageService> logger) : IFileStorageService
{
    private readonly string _basePath = Path.GetFullPath(options.Value.LocalBasePath);

    public async Task<StoredFile> StoreAsync(
        string fileName,
        Stream content,
        string contentType,
        string directory,
        CancellationToken cancellationToken)
    {
        string sanitizedFileName = SanitizeFileName(fileName);
        string uniqueName = $"{Guid.NewGuid():N}_{sanitizedFileName}";
        string relativePath = Path.Combine(directory, uniqueName).Replace('\\', '/');
        string absolutePath = GetAbsolutePath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using FileStream fileStream = File.Create(absolutePath);
        await content.CopyToAsync(fileStream, cancellationToken);

        long fileSize = fileStream.Length;

        logger.LogDebug("Stored file at {RelativePath} ({Bytes} bytes)", relativePath, fileSize);

        return new StoredFile(relativePath, fileSize);
    }

    public Task<Stream> RetrieveAsync(string storagePath, CancellationToken cancellationToken)
    {
        string absolutePath = GetAbsolutePath(storagePath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("Statement file not found.", storagePath);
        }

        return Task.FromResult<Stream>(File.OpenRead(absolutePath));
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        string absolutePath = GetAbsolutePath(storagePath);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken)
    {
        string absolutePath = GetAbsolutePath(storagePath);
        return Task.FromResult(File.Exists(absolutePath));
    }

    public Task<Uri?> GeneratePresignedDownloadUriAsync(
        string storagePath,
        TimeSpan expiry,
        CancellationToken cancellationToken) =>
        Task.FromResult<Uri?>(null);

    private string GetAbsolutePath(string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));

        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path traversal detected. Access denied.");
        }

        return fullPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
        return safe.Length > 100 ? safe[^100..] : safe;
    }
}
