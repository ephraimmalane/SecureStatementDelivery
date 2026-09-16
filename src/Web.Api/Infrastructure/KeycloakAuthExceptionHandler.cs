using System.Net;
using Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Infrastructure;

/// <summary>
/// Translates transient Keycloak failures (rate limiting, upstream 5xx, unreachable) into a
/// 503 response. Non-transient Keycloak responses (e.g. a 400, which means our request was
/// malformed) are deliberately left for <see cref="GlobalExceptionHandler"/> to log and return
/// as a 500, so genuine defects surface instead of being masked as retryable unavailability.
/// </summary>
internal sealed class KeycloakAuthExceptionHandler(ILogger<KeycloakAuthExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not IdentityProviderException keycloakException ||
            !IsTransient(keycloakException.StatusCode))
        {
            return false;
        }

        string correlationId = httpContext.TraceIdentifier;

        logger.LogWarning(
            "Keycloak unavailable ({StatusCode}) — CorrelationId: {CorrelationId}",
            (int)keycloakException.StatusCode,
            correlationId);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.4",
            Title = "Service unavailable",
            Detail = "The authentication service is temporarily unavailable. Please try again later.",
            Extensions = { ["correlationId"] = correlationId }
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
