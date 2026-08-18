using Infrastructure.Keycloak;
using Shouldly;

namespace Infrastructure.UnitTests.Keycloak;

public class KeycloakAdminTokenCacheTests
{
    private static KeycloakTokenResponse Token(string accessToken, int expiresIn) =>
        new(accessToken, "refresh", expiresIn, "Bearer");

    [Fact]
    public async Task GetTokenAsync_Should_ReturnCachedToken_WithoutRefetching_WhenNotExpired()
    {
        using var cache = new KeycloakAdminTokenCache();
        int calls = 0;

        Task<KeycloakTokenResponse> Fetch(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Token("t1", expiresIn: 3600));
        }

        string first = await cache.GetTokenAsync(Fetch, CancellationToken.None);
        string second = await cache.GetTokenAsync(Fetch, CancellationToken.None);

        first.ShouldBe("t1");
        second.ShouldBe("t1");
        calls.ShouldBe(1); // second call served from the cache — no extra login
    }

    [Fact]
    public async Task GetTokenAsync_Should_Refetch_WhenCachedTokenIsWithinExpiryBuffer()
    {
        using var cache = new KeycloakAdminTokenCache();
        int calls = 0;

        Task<KeycloakTokenResponse> Fetch(CancellationToken _)
        {
            int n = Interlocked.Increment(ref calls);
            // ExpiresIn is inside the 30s refresh buffer, so the token is always treated as expiring.
            return Task.FromResult(Token($"t{n}", expiresIn: 5));
        }

        string first = await cache.GetTokenAsync(Fetch, CancellationToken.None);
        string second = await cache.GetTokenAsync(Fetch, CancellationToken.None);

        first.ShouldBe("t1");
        second.ShouldBe("t2"); // refreshed because the cached token was within the expiry buffer
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task GetTokenAsync_Should_PerformSingleLogin_UnderConcurrency()
    {
        using var cache = new KeycloakAdminTokenCache();
        int calls = 0;

        async Task<KeycloakTokenResponse> Fetch(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            // Hold the refresh long enough that the concurrent callers pile up behind the lock.
            await Task.Delay(50, ct);
            return Token("t1", expiresIn: 3600);
        }

        Task<string>[] tasks = Enumerable
            .Range(0, 32)
            .Select(_ => cache.GetTokenAsync(Fetch, CancellationToken.None))
            .ToArray();

        string[] results = await Task.WhenAll(tasks);

        calls.ShouldBe(1); // the stampede collapsed into a single login
        results.ShouldAllBe(t => t == "t1");
    }
}
