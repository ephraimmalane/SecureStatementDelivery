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

builder.AddServiceDefaults();

builder.Host.UseSerilog(
    (context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration),
    writeToProviders: true);

long maxUploadBytes = builder.Configuration
    .GetSection(StorageOptions.SectionName)
    .Get<StorageOptions>()?.MaxUploadBytes ?? 50L * 1024 * 1024;

builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = maxUploadBytes);

builder.Services.Configure<FormOptions>(form =>
{
    form.MultipartBodyLengthLimit = maxUploadBytes;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    string[] knownNetworks = builder.Configuration
        .GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];

    foreach (string cidr in knownNetworks)
    {
        if (System.Net.IPNetwork.TryParse(cidr, out System.Net.IPNetwork network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }
});

builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

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

builder.Services.AddStatementIngestion(builder.Configuration);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

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

static string GetRateLimitPartitionKey(HttpContext httpContext)
{
    string? userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.FindFirstValue("sub");

    return !string.IsNullOrEmpty(userId)
        ? $"user:{userId}"
        : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

WebApplication app = builder.Build();

if (args.Contains("--migrate"))
{
    app.ApplyMigrations();
    return;
}

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
    opts.IncludeQueryInRequestPath = false;
});

app.UseExceptionHandler();

app.UseSecurityHeaders();

app.UseCors("DefaultCors");

app.UseAuthentication();

app.UseAuthorization();

app.MapEndpoints();

app.MapResumableUploadEndpoint();

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
