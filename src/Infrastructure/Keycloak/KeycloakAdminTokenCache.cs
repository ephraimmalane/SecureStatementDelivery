namespace Infrastructure.Keycloak;

// Singleton. Caches the Keycloak master admin token across the (transient) KeycloakClient instances,
// so admin operations no longer each perform a fresh password-grant login. The token is refreshed
// shortly before it expires, and a SemaphoreSlim collapses concurrent refreshes into a single login
// (no cache stampede). The fast path is lock-free: a valid cached token is returned without waiting.
internal sealed class KeycloakAdminTokenCache : IDisposable
{
    // Refresh this far ahead of the real expiry so a token handed to a caller is never on the verge
    // of expiring mid-request.
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile string? _token;
    private long _expiresAtTicks;

    public async Task<string> GetTokenAsync(
        Func<CancellationToken, Task<KeycloakTokenResponse>> fetch,
        CancellationToken cancellationToken)
    {
        string? cached = _token;
        if (cached is not null && !IsExpiring())
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed the token while we waited on the lock.
            cached = _token;
            if (cached is not null && !IsExpiring())
            {
                return cached;
            }

            KeycloakTokenResponse response = await fetch(cancellationToken);

            // Publish the expiry before the token (a volatile write) so any reader that observes the
            // new token is guaranteed to also observe its matching expiry.
            Interlocked.Exchange(
                ref _expiresAtTicks,
                DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn).Ticks);
            _token = response.AccessToken;

            return response.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsExpiring() =>
        DateTimeOffset.UtcNow.Ticks >= Interlocked.Read(ref _expiresAtTicks) - ExpiryBuffer.Ticks;

    public void Dispose() => _refreshLock.Dispose();
}
