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
        using var cache = new KeycloakAdminTokenCache(TimeProvider.System);
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
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task GetTokenAsync_Should_Refetch_WhenCachedTokenIsWithinExpiryBuffer()
    {
        using var cache = new KeycloakAdminTokenCache(TimeProvider.System);
        int calls = 0;

        Task<KeycloakTokenResponse> Fetch(CancellationToken _)
        {
            int n = Interlocked.Increment(ref calls);
            return Task.FromResult(Token($"t{n}", expiresIn: 5));
        }

        string first = await cache.GetTokenAsync(Fetch, CancellationToken.None);
        string second = await cache.GetTokenAsync(Fetch, CancellationToken.None);

        first.ShouldBe("t1");
        second.ShouldBe("t2");
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task GetTokenAsync_Should_PerformSingleLogin_UnderConcurrency()
    {
        using var cache = new KeycloakAdminTokenCache(TimeProvider.System);
        int calls = 0;

        async Task<KeycloakTokenResponse> Fetch(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50, ct);
            return Token("t1", expiresIn: 3600);
        }

        Task<string>[] tasks = Enumerable
            .Range(0, 32)
            .Select(_ => cache.GetTokenAsync(Fetch, CancellationToken.None))
            .ToArray();

        string[] results = await Task.WhenAll(tasks);

        calls.ShouldBe(1);
        results.ShouldAllBe(t => t == "t1");
    }
}
