namespace Application.Abstractions.Cache;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default);

    // Binary get/set for cached blobs (e.g. consolidated PDFs) — stored as raw bytes, with no JSON
    // wrapping, so large values aren't inflated.
    Task<byte[]?> GetBytesAsync(string key, CancellationToken cancellationToken = default);

    Task SetBytesAsync(string key, byte[] value, TimeSpan expiry, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
