using System.Data.Common;
using System.Net;
using Infrastructure.Database;
using Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IntegrationTests;

// Boots the real Web.Api pipeline (middleware, auth, rate limiting, endpoints) against an
// in-memory SQLite database instead of PostgreSQL, so the suite runs anywhere — including CI
// without Docker. Swap to Testcontainers + UseNpgsql when Postgres-specific behaviour (jsonb,
// FOR UPDATE SKIP LOCKED, partial indexes) needs to be exercised.
public sealed class StatementDeliveryWebApplicationFactory : WebApplicationFactory<Program>
{
    // A single shared, open connection keeps the in-memory database alive for the host's lifetime.
    private readonly DbConnection _connection = new SqliteConnection("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" disables the Development-only Swagger + auto-migration branches in Program.cs.
        builder.UseEnvironment("Testing");

        // UseSetting writes straight into host configuration, so these are visible while Program.cs
        // builds its services (AddInfrastructure reads connection strings there). A ConfigureApp-
        // Configuration source would be merged too late and AddNpgSql would see a null string.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["ConnectionStrings:Redis"] = "",
            ["Keycloak:BaseUrl"] = "https://keycloak.test",
            ["Keycloak:Authority"] = "https://keycloak.test/realms/secure-statements",
            ["Keycloak:Realm"] = "secure-statements",
            ["Keycloak:ClientId"] = "secure-statement-delivery",
            ["DownloadToken:Secret"] = "test-download-token-secret-0123456789",
            ["DownloadToken:Issuer"] = "statement-download",
            // Base64 of 32 zero bytes — a valid 256-bit AES key for tests only.
            ["FieldEncryption:Key"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            ["Storage:Provider"] = "Local",
            ["Storage:LocalBasePath"] = Path.Combine(Path.GetTempPath(), "ssd-tests-" + Guid.NewGuid()),
            ["Storage:MaxUploadBytes"] = "52428800"
        };

        foreach (KeyValuePair<string, string?> setting in settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureTestServices(services =>
        {
            _connection.Open();

            // Remove every EF descriptor tied to the Npgsql registration. Dropping only the options
            // descriptor leaves the Npgsql provider registered, and EF refuses two providers in one
            // container (IDbContextOptionsConfiguration<T> is what applies UseNpgsql in EF 10).
            var efDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(ApplicationDbContext) ||
                    // IDbContextOptionsConfiguration<ApplicationDbContext> (EF 9+) is what applies
                    // UseNpgsql; matched by name to avoid a namespace-specific import.
                    d.ServiceType.Name == "IDbContextOptionsConfiguration`1")
                .ToList();

            foreach (ServiceDescriptor descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>((sp, options) =>
                options
                    .UseSqlite(_connection)
                    .UseSnakeCaseNamingConvention()
                    .UseFieldEncryption(sp.GetRequiredService<IFieldEncryptor>()));

            // The OutboxProcessor uses Postgres-only SQL; drop background services for tests.
            services.RemoveAll<IHostedService>();

            // TestServer has no real socket, so HttpContext.Connection.RemoteIpAddress is null and
            // the IP-binding branch of the download handler can't be exercised over HTTP. This
            // filter lets a test set the perceived client IP via a header. It is inert unless the
            // header is present, so it never affects the other suites.
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

    // Header name a test uses to spoof the source IP of a request (see TestClientIpStartupFilter).
    public const string TestClientIpHeader = "X-Test-Client-Ip";

    private sealed class TestClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(TestClientIpHeader, out Microsoft.Extensions.Primitives.StringValues value) &&
                        IPAddress.TryParse(value.ToString(), out IPAddress? address))
                    {
                        context.Connection.RemoteIpAddress = address;
                    }

                    await nextMiddleware();
                });

                next(app);
            };
    }

    // Create the SQLite schema once the real service provider exists (building an intermediate
    // provider inside ConfigureTestServices breaks EF's internal service resolution).
    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = base.CreateHost(builder);

        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
