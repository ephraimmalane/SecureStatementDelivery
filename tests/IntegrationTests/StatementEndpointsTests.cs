using System.Net;
using Shouldly;

namespace IntegrationTests;

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

        ((int)response.StatusCode).ShouldBeInRange(400, 499);
    }
}
