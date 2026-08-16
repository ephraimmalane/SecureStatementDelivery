using Amazon.S3;
using Application.Abstractions.Authentication;
using Application.Abstractions.Cache;
using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Infrastructure.Authentication;
using Infrastructure.Authorization;
using Infrastructure.Cache;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Infrastructure.Keycloak;
using Infrastructure.Outbox;
using Infrastructure.Security;
using Infrastructure.Storage;
using Infrastructure.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using nClam;
using SharedKernel;

namespace Infrastructure;

public static class DependencyInjection
{
    // Total time the per-user authorization result (active check) lives in the distributed L2.
    // Kept short so a suspended user loses access quickly even without explicit invalidation.
    internal static readonly TimeSpan AuthorizationCacheTtl = TimeSpan.FromSeconds(60);

    // L1 (in-process) lifetime — shorter than L2 so each replica re-validates frequently while
    // still serving the overwhelming majority of requests from local memory.
    internal static readonly TimeSpan AuthorizationLocalCacheTtl = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services
            .AddServices()
            .AddFieldEncryption(configuration)
            .AddDatabase(configuration)
            .AddOutbox(configuration)
            .AddRedisCache(configuration)
            .AddFileStorage(configuration)
            .AddContentScanning(configuration)
            .AddHealthChecks(configuration)
            .AddKeycloak(configuration)
            .AddAuthorizationInternal();

    private static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddTransient<IDomainEventsDispatcher, DomainEventsDispatcher>();
        services.AddSingleton<Observability.StatementMetrics>();
        return services;
    }

    private static IServiceCollection AddFieldEncryption(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FieldEncryptionOptions>(
            configuration.GetSection(FieldEncryptionOptions.SectionName));

        string? key = configuration[$"{FieldEncryptionOptions.SectionName}:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("FieldEncryption:Key is not configured.");
        }

        // Fail fast at startup on a malformed/short key rather than at first encrypt.
        Span<byte> buffer = stackalloc byte[33];
        if (!Convert.TryFromBase64String(key, buffer, out int written) || written != 32)
        {
            throw new InvalidOperationException(
                "FieldEncryption:Key must be a base64-encoded 256-bit (32-byte) key.");
        }

        services.AddSingleton<IFieldEncryptor, AesFieldEncryptor>();

        return services;
    }

    private static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Database");

        // Pooled contexts are reset and reused instead of allocated per request, cutting per-request
        // allocation/GC under high throughput. The encryptor (a singleton) is attached via the
        // options because pooling requires ApplicationDbContext to expose only a
        // DbContextOptions-only constructor.
        services.AddDbContextPool<ApplicationDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        Schemas.Default);

                    // Retry transient faults (network blips, Postgres failover, deadlocks) instead of
                    // surfacing them straight to the caller. The execution strategy is idempotent for
                    // our unit-of-work-per-request usage; explicit transactions must use
                    // IExecutionStrategy.ExecuteAsync (see OutboxProcessor).
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null);
                })
                .UseSnakeCaseNamingConvention()
                .UseFieldEncryption(serviceProvider.GetRequiredService<IFieldEncryptor>()));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

        return services;
    }

    private static IServiceCollection AddOutbox(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddHostedService<OutboxProcessor>();
        return services;
    }

    private static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? redisConnection = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICacheService, CacheService>();

        // HybridCache layers an in-process L1 cache over the distributed L2 (Redis above) and
        // adds built-in stampede protection (one factory call per key, not one per concurrent
        // request). The hot authorization path (PermissionProvider) reads through it, so the
        // per-request active-user check is served from process memory instead of hitting Postgres
        // on every call. If Redis is unavailable the L1 cache still absorbs the load.
        services.AddHybridCache(options =>
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = AuthorizationCacheTtl,
                LocalCacheExpiration = AuthorizationLocalCacheTtl
            });

        return services;
    }

    private static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        // Stateless PDF encryption helper used by both upload paths.
        services.AddSingleton<IPdfProtector, PdfSharpPdfProtector>();

        // Merges multiple statement PDFs into one (consolidated multi-month download).
        services.AddSingleton<IPdfConsolidator, PdfSharpPdfConsolidator>();

        StorageOptions storageOptions = configuration
            .GetSection(StorageOptions.SectionName)
            .Get<StorageOptions>() ?? new StorageOptions();

        if (storageOptions.Provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
        {
            var s3Config = new AmazonS3Config { ForcePathStyle = storageOptions.S3.ForcePathStyle };

            if (!string.IsNullOrWhiteSpace(storageOptions.S3.Region))
            {
                s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(storageOptions.S3.Region);
            }

            if (!string.IsNullOrWhiteSpace(storageOptions.S3.ServiceUrl))
            {
                s3Config.ServiceURL = storageOptions.S3.ServiceUrl;
            }

            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(s3Config));
            services.AddSingleton<IFileStorageService, S3FileStorageService>();
        }
        else
        {
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        }

        return services;
    }

    // Anti-malware scanning. ClamAV (clamd over TCP) when enabled; otherwise a no-op pass-through
    // so local/dev runs don't require an AV engine. Internal so it can be unit-tested directly.
    internal static IServiceCollection AddContentScanning(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ClamAvOptions>(configuration.GetSection(ClamAvOptions.SectionName));

        ClamAvOptions options = configuration
            .GetSection(ClamAvOptions.SectionName)
            .Get<ClamAvOptions>() ?? new ClamAvOptions();

        if (options.Enabled)
        {
            // ClamClient just records host/port; it doesn't connect until a scan runs.
            services.AddSingleton<IClamClient>(_ => new ClamClient(options.Host, options.Port)
            {
                MaxStreamSize = options.MaxStreamSizeBytes
            });
            services.AddSingleton<IFileContentScanner, ClamAvFileContentScanner>();
        }
        else
        {
            services.AddSingleton<IFileContentScanner, NoOpFileContentScanner>();
        }

        return services;
    }

    private static IServiceCollection AddHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IHealthChecksBuilder healthBuilder = services.AddHealthChecks()
            .AddNpgSql(configuration.GetConnectionString("Database")!);

        string? redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            healthBuilder.AddRedis(redisConnection);
        }

        return services;
    }

    private static IServiceCollection AddKeycloak(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        KeycloakOptions keycloakOptions = configuration
            .GetSection(KeycloakOptions.SectionName)
            .Get<KeycloakOptions>()
            ?? throw new InvalidOperationException("Keycloak configuration section is missing.");

        if (string.IsNullOrWhiteSpace(keycloakOptions.BaseUrl))
        {
            throw new InvalidOperationException("Keycloak:BaseUrl is not configured.");
        }

        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName));

        // Typed HttpClient for all Keycloak API calls.
        services.AddHttpClient<IKeycloakClient, KeycloakClient>();

        // JWT validation delegates to Keycloak's JWKS endpoint via Authority discovery.
        // RequireHttpsMetadata is false because TLS terminates at the Nginx Ingress —
        // the pod receives plain HTTP from within the cluster.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = keycloakOptions.Authority;
                o.Audience = keycloakOptions.ClientId;
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ClockSkew = TimeSpan.Zero,
                    // Keycloak signs with RS256 by default; pinning prevents algorithm-confusion attacks.
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                };

                // Surface the exact reason a bearer token is rejected (issuer/signature/metadata),
                // logged at Warning so it is visible even with Microsoft logging at Warning level.
                o.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Keycloak.JwtBearer")
                            .LogWarning(
                                context.Exception,
                                "JWT authentication failed: {Reason}",
                                context.Exception.Message);
                        return Task.CompletedTask;
                    }
                };
            });

        // Download tokens are still our own HS256 JWTs — Keycloak does not manage them.
        services.Configure<DownloadTokenOptions>(
            configuration.GetSection(DownloadTokenOptions.SectionName));

        string downloadSecret = configuration[$"{DownloadTokenOptions.SectionName}:Secret"]
            ?? throw new InvalidOperationException("DownloadToken:Secret is not configured.");
        if (downloadSecret.Length < 32)
        {
            throw new InvalidOperationException("DownloadToken:Secret must be at least 32 characters (256 bits).");
        }

        // Previous keys (rotation overlap) must meet the same strength bar — a weak retained key
        // would still validate live tokens.
        string[] previousSecrets = configuration
            .GetSection($"{DownloadTokenOptions.SectionName}:{nameof(DownloadTokenOptions.PreviousSecrets)}")
            .Get<string[]>() ?? [];
        if (Array.Exists(previousSecrets, s => !string.IsNullOrWhiteSpace(s) && s.Length < 32))
        {
            throw new InvalidOperationException(
                "Every DownloadToken:PreviousSecrets entry must be at least 32 characters (256 bits).");
        }

        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();
        services.AddSingleton<IDownloadTokenService, DownloadTokenService>();

        return services;
    }

    private static IServiceCollection AddAuthorizationInternal(this IServiceCollection services)
    {
        // The ingestion policy is a named policy so the custom policy provider returns it directly
        // (via the base AuthorizationOptions lookup) instead of falling through to a per-user
        // PermissionRequirement — a service account has no domain User to check for active status.
        // It authorizes purely on the Keycloak realm role carried by the client_credentials token.
        services.AddAuthorizationBuilder()
            .AddPolicy(IngestionAuthorization.PolicyName, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasRealmRole(IngestionAuthorization.Role)));

        services.AddScoped<PermissionProvider>();
        services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddTransient<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        return services;
    }
}
