using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace IntegrationTests;

public sealed class AdminUserEndpointsTests(StatementDeliveryWebApplicationFactory factory)
    : IClassFixture<StatementDeliveryWebApplicationFactory>
{
    private readonly StatementDeliveryWebApplicationFactory _factory = factory;

    [Fact]
    public async Task SetCustomerIdNumber_Should_Return401_When_NotAuthenticated()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/customers/{Guid.NewGuid()}/south-african-id", UriKind.Relative),
            new { southAfricanIdNumber = "8001015009087" });

        // The endpoint requires the Admin.Users permission; an anonymous caller is rejected.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
