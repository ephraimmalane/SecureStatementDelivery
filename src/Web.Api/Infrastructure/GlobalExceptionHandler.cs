using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Infrastructure;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Log the exception type and correlation ID without serialising the full exception object.
        // Logging exception.Message is unsafe — it can contain request data (e.g. HTTP response bodies
        // from downstream calls). The CorrelationId links this entry to the full request trace in Seq.
        string correlationId = httpContext.TraceIdentifier;

        logger.LogError(
            "Unhandled {ExceptionType} — CorrelationId: {CorrelationId}",
            exception.GetType().FullName,
            correlationId);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
            Title = "Server failure",
            Extensions = { ["correlationId"] = correlationId }
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
