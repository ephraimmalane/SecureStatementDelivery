using System.Text;
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
using Infrastructure.Storage;
using Infrastructure.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using SharedKernel;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services
            .AddServices()
            .AddDatabase(configuration)
            .AddRedisCache(configuration)
            .AddFileStorage(configuration)
            .AddHealthChecks(configuration)
            .AddAuthenticationInternal(configuration)
            .AddAuthorizationInternal();

    private static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddTransient<IDomainEventsDispatcher, DomainEventsDispatcher>();
        return services;
    }

    private static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Database");

        services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseNpgsql(connectionString, npgsqlOptions =>
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        Schemas.Default))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

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

        return services;
    }

    private static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

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

    private static IServiceCollection AddAuthenticationInternal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!)),
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.Configure<DownloadTokenOptions>(
            configuration.GetSection(DownloadTokenOptions.SectionName));

        services.Configure<TokenOptions>(
            configuration.GetSection(TokenOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenProvider, TokenProvider>();
        services.AddSingleton<IDownloadTokenService, DownloadTokenService>();

        return services;
    }

    private static IServiceCollection AddAuthorizationInternal(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddScoped<PermissionProvider>();
        services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddTransient<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        return services;
    }
}
