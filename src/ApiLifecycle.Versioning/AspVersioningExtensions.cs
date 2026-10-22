using Asp.Versioning;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApiLifecycle;

public static class AspVersioningExtensions
{
    /// <summary>Use canonical Asp.Versioning keys such as "1.0" in the registry.</summary>
    public static IServiceCollection AddApiLifecycleVersioning(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IApiLifecycleVersionResolver, AspVersioningResolver>());
        return services;
    }
}

internal sealed class AspVersioningResolver : IApiLifecycleVersionResolver
{
    public string? Resolve(HttpContext context) => context.Features.Get<IApiVersioningFeature>()?.RequestedApiVersion?.ToString("FF", CultureInfo.InvariantCulture);

    public IReadOnlyCollection<string> GetDeclaredVersions(Endpoint endpoint)
    {
        var metadata = endpoint.Metadata.GetMetadata<ApiVersionMetadata>();
        return metadata?.Map(ApiVersionMapping.Explicit | ApiVersionMapping.Implicit).DeclaredApiVersions
            .Select(version => version.ToString("FF", CultureInfo.InvariantCulture)).ToArray() ?? [];
    }

    public bool IsVersionNeutral(Endpoint endpoint) => endpoint.Metadata.GetMetadata<ApiVersionMetadata>()?.IsApiVersionNeutral == true;
}
