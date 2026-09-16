using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Web.Api.Features;

namespace Web.Api.Extensions;

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ServiceDescriptor[] serviceDescriptors = assembly
            .DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false } &&
                           type.IsAssignableTo(typeof(IEndpoint)))
            .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type))
            .ToArray();

        services.TryAddEnumerable(serviceDescriptors);

        return services;
    }

    public static IApplicationBuilder MapEndpoints(
        this WebApplication app,
        RouteGroupBuilder? routeGroupBuilder = null)
    {
        IEnumerable<IEndpoint> endpoints = app.Services.GetRequiredService<IEnumerable<IEndpoint>>();

        IEndpointRouteBuilder builder = routeGroupBuilder is null ? app : routeGroupBuilder;

        bool isProduction = app.Environment.IsProduction();

        foreach (IEndpoint endpoint in endpoints)
        {
            if (isProduction && endpoint is IDevelopmentOnlyEndpoint)
            {
                continue;
            }

            endpoint.MapEndpoint(builder);
        }

        return app;
    }

    public static RouteHandlerBuilder HasPermission(this RouteHandlerBuilder app, string permission)
    {
        return app.RequireAuthorization(permission).RequireRateLimiting("api");
    }

    public static RouteGroupBuilder MapAuthGroup(this IEndpointRouteBuilder app) =>
        app.MapGroup("auth")
            .WithTags(Tags.Auth)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
}
