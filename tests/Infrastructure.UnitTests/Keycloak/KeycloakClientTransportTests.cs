using System.Net;
using Application.Abstractions.Authentication;
using Infrastructure.Keycloak;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Infrastructure.UnitTests.Keycloak;

public sealed class KeycloakClientTransportTests
{
    [Fact]
    public async Task LoginAsync_Should_WrapUnreachableKeycloak_As503KeycloakAuthException()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("no route to host"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://keycloak.test") };
        using var cache = new KeycloakAdminTokenCache(TimeProvider.System);
        var client = new KeycloakClient(httpClient, CreateOptions(), cache);

        IdentityProviderException exception = await Should.ThrowAsync<IdentityProviderException>(
            () => client.LoginAsync("user@example.com", "pw", CancellationToken.None));

        exception.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    private static IOptions<KeycloakOptions> CreateOptions() =>
        Options.Create(new KeycloakOptions
        {
            BaseUrl = "https://keycloak.test",
            Realm = "secure-statements",
            ClientId = "client",
            ClientSecret = "secret",
            AdminUsername = "admin",
            AdminPassword = "password"
        });

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
