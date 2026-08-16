using System.Net;
using Shouldly;

namespace IntegrationTests;

// Exercises the full HTTP pipeline (routing, auth, rate limiting, endpoint) end-to-end. These run
// offline against in-memory SQLite — no Keycloak token is presented, which is exactly what lets us
// assert the security defaults (protected endpoints reject anonymous callers; the public download
// endpoint rejects junk tokens without leaking a 500).
public sealed class StatementEndpointsTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task GetStatements_Should_Return401_When_NoBearerTokenPresented()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/statements", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConsolidatedStatement_Should_Return401_When_NoBearerTokenPresented()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/statements/consolidated?from=2024-01&to=2024-03", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DownloadStatement_Should_RejectInvalidToken_WithoutServerError()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/statements/download?token=not-a-real-token", UriKind.Relative));

        // The single-use download endpoint is anonymous (the token is the credential). A malformed
        // token must be a client error, never a 5xx (which would imply an unhandled exception).
        ((int)response.StatusCode).ShouldBeInRange(400, 499);
    }
}
