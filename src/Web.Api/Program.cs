using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Application;
using HealthChecks.UI.Client;
using Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Infrastructure.Storage;
using Serilog;
using Web.Api;
using Web.Api.Extensions;
using Web.Api.Features.Statements.Ingestion;
using Web.Api.Features.Statements.ResumableUpload;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, liveness checks, service discovery, HTTP resilience.
// Safe to call outside Aspire too — the OTLP exporter is enabled only when Aspire injects
// OTEL_EXPORTER_OTLP_ENDPOINT, so docker-compose / Kubernetes runs are unaffected.
builder.AddServiceDefaults();

// writeToProviders: true so Serilog also forwards events to the registered ILoggerProviders —
// notably the OpenTelemetry logging provider from AddServiceDefaults — which exports them over OTLP
// to the Aspire dashboard's Structured Logs. Without it, Serilog owns the pipeline and the dashboard
// shows no logs (traces/metrics still appear because they bypass the logging pipeline).
builder.Host.UseSerilog(
    (context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration),
    writeToProviders: true);

// Max upload size is configured once in StorageOptions and reused everywhere.
// Kestrel's default (28.6 MB) would reject large requests before the domain validator runs.
long maxUploadBytes = builder.Configuration
    .GetSection(StorageOptions.SectionName)
    .Get<StorageOptions>()?.MaxUploadBytes ?? 50L * 1024 * 1024;

builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = maxUploadBytes);

// Multipart form parsing must allow the same size, otherwise the form pipeline rejects
// the request before the Kestrel body-size check is even relevant.
builder.Services.Configure<FormOptions>(form =>
{
    form.MultipartBodyLengthLimit = maxUploadBytes;
});

// TLS terminates at the Nginx Ingress, so the pod receives plain HTTP with the real client IP
// in X-Forwarded-For / X-Forwarded-Proto. Without this, RemoteIpAddress is the ingress pod IP —
// which would collapse all per-IP rate-limit partitions and IP-bound download tokens into one.
// KnownNetworks/KnownProxies are cleared because the ingress IP is not stable in the cluster;
// the NetworkPolicy (default-deny) is what restricts who can reach the pod.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    // Only trust X-Forwarded-* from the ingress network(s). Without this, anything that can reach
    // the pod could spoof the client IP/scheme and bypass IP-bound download tokens and per-IP rate
    // limiting. Set ForwardedHeaders:KnownNetworks to the ingress-controller pod CIDR in production.
    string[] knownNetworks = builder.Configuration
        .GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];

    foreach (string cidr in knownNetworks)
    {
        if (System.Net.IPNetwork.TryParse(cidr, out System.Net.IPNetwork network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }
    // When KnownNetworks is empty (local/dev), the list stays cleared and X-Forwarded-For is
    // trusted from any caller — acceptable only behind the NetworkPolicy default-deny.
});

// Drain in-flight uploads/downloads on SIGTERM before the pod is killed, so rolling deploys
// don't sever transfers mid-stream. Keep this below Kubernetes terminationGracePeriodSeconds.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

// CORS for browser SPAs calling the API cross-origin. Origins are configuration-driven; with
// none configured the policy admits no cross-origin caller (fail-closed, not wildcard).
string[] corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddPolicy("DefaultCors", policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    }));

builder.Services.AddSwaggerGenWithAuth();

builder.Services
    .AddApplication()
    .AddPresentation()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddEndpoints(Assembly.GetExecutingAssembly());

builder.Services.AddResumableUploads();

// Pull-based ingestion (object-storage events). No-op unless Ingestion:Enabled is configured.
builder.Services.AddStatementIngestion(builder.Configuration);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partition each window by caller, not globally. A global limiter lets a single client
    // exhaust the whole window and lock everyone else out (a built-in DoS vector). The key is
    // the authenticated user (sub); anonymous callers — e.g. download-link redemption — fall
    // back to their client IP (made real by the ForwardedHeaders middleware above).
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("api", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            }));
});

// Resolves the rate-limit partition: authenticated user id when present, else client IP.
static string GetRateLimitPartitionKey(HttpContext httpContext)
{
    string? userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.FindFirstValue("sub");

    return !string.IsNullOrEmpty(userId)
        ? $"user:{userId}"
        : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

WebApplication app = builder.Build();

// One-shot migration mode for the pre-deploy Kubernetes Job (`args: ["--migrate"]`). Applies
// pending migrations and exits, so schema changes run exactly once per release instead of racing
// across every replica's startup. EF acquires a Postgres advisory lock, so even a stray second
// runner is safe.
if (args.Contains("--migrate"))
{
    app.ApplyMigrations();
    return;
}

// Must run before anything that reads the client IP or scheme (rate limiter, request logging,
// IP-bound download-token validation).
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerWithUi();
    app.ApplyMigrations();
}

app.UseRateLimiter();

app.UseRequestContextLogging();

app.UseSerilogRequestLogging(opts =>
{
    // Never include the query string in request logs.
    // GET /statements/download?token=<jwt> — the token is a credential.
    opts.IncludeQueryInRequestPath = false;
});

app.UseExceptionHandler();

app.UseSecurityHeaders();

app.UseCors("DefaultCors");

app.UseAuthentication();

app.UseAuthorization();

app.MapEndpoints();

app.MapResumableUploadEndpoint();

// Aspire liveness endpoint (/alive). The rich /health below keeps the PostgreSQL + Redis
// readiness checks used by Kubernetes probes.
app.MapDefaultEndpoints();

app.MapHealthChecks("health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

await app.RunAsync();

namespace Web.Api
{
    public partial class Program;
}
