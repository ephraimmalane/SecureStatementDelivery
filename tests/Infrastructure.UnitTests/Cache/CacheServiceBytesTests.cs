using System.Collections.Concurrent;
using Infrastructure.Cache;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Infrastructure.UnitTests.Cache;

public class CacheServiceBytesTests
{
    private static CacheService CreateService() =>
        new(new InMemoryDistributedCache(), NullLogger<CacheService>.Instance);

    [Fact]
    public async Task SetBytes_Then_GetBytes_Should_RoundTrip()
    {
        CacheService service = CreateService();
        byte[] data = [1, 2, 3, 4, 5];

        await service.SetBytesAsync("consolidated:key", data, TimeSpan.FromMinutes(5));

        (await service.GetBytesAsync("consolidated:key")).ShouldBe(data);
    }

    [Fact]
    public async Task GetBytes_Should_Return_Null_On_Miss()
    {
        CacheService service = CreateService();

        (await service.GetBytesAsync("missing")).ShouldBeNull();
    }

    // Minimal in-memory IDistributedCache so the binary cache path can be tested without Redis.
    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new();

        public byte[]? Get(string key) => _store.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(_store.GetValueOrDefault(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _store.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            _store.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
