using Infrastructure.Authorization;
using tusdotnet;

namespace Web.Api.Features.Statements.ResumableUpload;

public static class ResumableUploadExtensions
{
    public static IServiceCollection AddResumableUploads(this IServiceCollection services)
    {
        services.AddSingleton<TusStorageProvider>();
        services.AddScoped<ResumableUploadCompletedHandler>();
        return services;
    }

    // Maps the TUS resumable-upload protocol endpoint. The route is protected by the same
    // Statements.Upload permission policy as the regular multipart upload endpoint, so the
    // JWT bearer + permission check run before any chunk is accepted.
    public static void MapResumableUploadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapTus("/statements/upload/resumable", static httpContext =>
        {
            TusStorageProvider provider = httpContext.RequestServices.GetRequiredService<TusStorageProvider>();
            return Task.FromResult(provider.Configuration);
        })
        .RequireAuthorization(Permissions.StatementsUpload)
        .RequireRateLimiting("api");
    }
}
