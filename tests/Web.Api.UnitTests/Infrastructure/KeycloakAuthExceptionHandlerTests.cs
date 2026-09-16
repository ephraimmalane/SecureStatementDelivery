using System.Net;
using Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Web.Api.Infrastructure;

namespace Web.Api.UnitTests.Infrastructure;

public sealed class KeycloakAuthExceptionHandlerTests
{
    private static readonly KeycloakAuthExceptionHandler Handler =
        new(NullLogger<KeycloakAuthExceptionHandler>.Instance);

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task TryHandleAsync_Should_Write503_ForTransientKeycloakFailures(HttpStatusCode statusCode)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        bool handled = await Handler.TryHandleAsync(
            context, new IdentityProviderException(statusCode, "boom"), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task TryHandleAsync_Should_NotHandle_NonTransientKeycloakFailures(HttpStatusCode statusCode)
    {
        var context = new DefaultHttpContext();

        bool handled = await Handler.TryHandleAsync(
            context, new IdentityProviderException(statusCode, "boom"), CancellationToken.None);

        handled.ShouldBeFalse();
    }

    [Fact]
    public async Task TryHandleAsync_Should_NotHandle_NonKeycloakExceptions()
    {
        var context = new DefaultHttpContext();

        bool handled = await Handler.TryHandleAsync(
            context, new InvalidOperationException("boom"), CancellationToken.None);

        handled.ShouldBeFalse();
    }
}
