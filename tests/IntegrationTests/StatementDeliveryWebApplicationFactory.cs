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

public sealed class StatementDeliveryWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly DbConnection _connection = new SqliteConnection("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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

            var efDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(ApplicationDbContext) ||
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

            services.RemoveAll<IHostedService>();

            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

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
