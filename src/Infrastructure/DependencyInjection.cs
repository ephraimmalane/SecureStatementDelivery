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
    internal static readonly TimeSpan AuthorizationCacheTtl = TimeSpan.FromSeconds(60);

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
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<IDomainEventsDispatcher, DomainEventsDispatcher>();
        services.AddSingleton<Observability.StatementMetrics>();
        return services;
    }

    private static IServiceCollection AddFieldEncryption(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<FieldEncryptionOptions>()
            .Bind(configuration.GetSection(FieldEncryptionOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options =>
                {
                    Span<byte> buffer = stackalloc byte[33];
                    return Convert.TryFromBase64String(options.Key, buffer, out int written) && written == 32;
                },
                "FieldEncryption:Key must be a base64-encoded 256-bit (32-byte) key.")
            .ValidateOnStart();

        services.AddSingleton<IFieldEncryptor, AesFieldEncryptor>();

        return services;
    }

    private static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Database");

        services.AddDbContextPool<ApplicationDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        Schemas.Default);

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

        services.AddSingleton<IFileTypeValidator, FileTypeValidator>();

        services.AddSingleton<IContentHasher, Sha256ContentHasher>();

        services.AddSingleton<IPdfProtector, PdfSharpPdfProtector>();

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

        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<KeycloakAdminTokenCache>();

        services.AddHttpClient<IKeycloakClient, KeycloakClient>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = keycloakOptions.Authority;
                o.Audience = keycloakOptions.ClientId;
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                };

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

        services.AddOptions<DownloadTokenOptions>()
            .Bind(configuration.GetSection(DownloadTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => Array.TrueForAll(
                    options.PreviousSecrets,
                    secret => string.IsNullOrWhiteSpace(secret) || secret.Length >= 32),
                "Every DownloadToken:PreviousSecrets entry must be at least 32 characters (256 bits).")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();
        services.AddSingleton<IDownloadTokenService, DownloadTokenService>();

        return services;
    }

    private static IServiceCollection AddAuthorizationInternal(this IServiceCollection services)
    {
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
