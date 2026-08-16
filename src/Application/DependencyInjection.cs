using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    // Handler scanning and validator registration moved to Web.Api.DependencyInjection (AddPresentation).
    // This method is retained for backward compatibility with Program.cs call order.
    public static IServiceCollection AddApplication(this IServiceCollection services) => services;
}
