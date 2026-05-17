using Application.Abstractions.Cache;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Cache;

internal sealed class CacheService(
    IDistributedCache cache,
    ILogger<CacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            byte[]? data = await cache.GetAsync(key, cancellationToken);

            if (data is null)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(data, SerializerOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache GET failed for key {Key}. Continuing without cache.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            };

            await cache.SetAsync(key, data, options, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache SET failed for key {Key}. Continuing without cache.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache REMOVE failed for key {Key}.", key);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            byte[]? data = await cache.GetAsync(key, cancellationToken);
            return data is not null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache EXISTS failed for key {Key}. Assuming not found.", key);
            return false;
        }
    }
}
