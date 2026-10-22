using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ApiLifecycle;

public static class ApiLifecycleServiceExtensions
{
    public static IServiceCollection AddApiLifecycle(this IServiceCollection services, Action<ApiLifecycleOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ApiLifecycleRegistry>();
        services.TryAddSingleton<IApiLifecycleRegistry>(provider => provider.GetRequiredService<ApiLifecycleRegistry>());
        services.TryAddSingleton<IApiLifecycleVersionResolver, MetadataVersionResolver>();
        services.TryAddSingleton<ApiLifecycleMetrics>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, LifecycleStartupFilter>());
        return services;
    }

    public static IServiceCollection AddApiLifecycle(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiLifecycle(_ => { });
        services.AddOptions<ApiLifecycleOptions>().Bind(configuration);
        return services;
    }

    /// <summary>Call after routing and authorization, before endpoints execute.</summary>
    public static IApplicationBuilder UseApiLifecycle(this IApplicationBuilder app) => app.UseMiddleware<ApiLifecycleMiddleware>();
}

internal sealed class LifecycleStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        var registry = app.ApplicationServices.GetRequiredService<IApiLifecycleRegistry>();
        next(app);
        if (!app.ApplicationServices.GetRequiredService<IOptions<ApiLifecycleOptions>>().Value.StrictMode) return;
        var resolver = app.ApplicationServices.GetRequiredService<IApiLifecycleVersionResolver>();
        var sources = app.ApplicationServices.GetRequiredService<EndpointDataSource>();
        foreach (var endpoint in sources.Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<SkipApiLifecycleMetadata>() is not null || resolver.IsVersionNeutral(endpoint)) continue;
            var versions = resolver.GetDeclaredVersions(endpoint);
            if (versions.Count == 0 || versions.Any(version => !registry.TryGetPolicy(version, out _)))
                throw new InvalidOperationException($"Endpoint '{endpoint.DisplayName}' must declare registered lifecycle versions or explicitly skip ApiLifecycle.");
        }
    };
}
