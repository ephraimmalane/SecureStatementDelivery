namespace Infrastructure.Keycloak;

internal sealed class KeycloakAdminTokenCache(TimeProvider timeProvider) : IDisposable
{
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
            cached = _token;
            if (cached is not null && !IsExpiring())
            {
                return cached;
            }

            KeycloakTokenResponse response = await fetch(cancellationToken);

            Interlocked.Exchange(
                ref _expiresAtTicks,
                timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn).UtcTicks);
            _token = response.AccessToken;

            return response.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsExpiring() =>
        timeProvider.GetUtcNow().UtcTicks >= Interlocked.Read(ref _expiresAtTicks) - ExpiryBuffer.Ticks;

    public void Dispose() => _refreshLock.Dispose();
}
