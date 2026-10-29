using Asp.Versioning;
using System.Globalization;
using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ApiLifecycle;

public static class AspVersioningExtensions
{
    /// <summary>Resolve lifecycle keys using Asp.Versioning's parsed version equality.</summary>
    public static IServiceCollection AddApiLifecycleVersioning(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IApiLifecycleVersionResolver, AspVersioningResolver>());
        return services;
    }
}

internal sealed class AspVersioningResolver : IApiLifecycleVersionResolver
{
    private readonly FrozenDictionary<ApiVersion, string> keys;

    public AspVersioningResolver(IApiLifecycleRegistry registry, IApiVersionParser parser)
    {
        var parsed = new Dictionary<ApiVersion, string>();
        var errors = new List<string>();
        foreach (var policy in registry.Policies)
        {
            if (!parser.TryParse(policy.Version.AsSpan(), out var version))
                errors.Add($"Lifecycle key '{policy.Version}' is not a valid Asp.Versioning version.");
            else if (!parsed.TryAdd(version, policy.Version))
                errors.Add($"Lifecycle keys '{parsed[version]}' and '{policy.Version}' represent the same API version.");
        }
        if (errors.Count != 0)
            throw new OptionsValidationException(nameof(ApiLifecycleOptions), typeof(ApiLifecycleOptions), errors);
        keys = parsed.ToFrozenDictionary();
    }

    private string Key(ApiVersion version) => keys.TryGetValue(version, out var key)
        ? key : version.ToString("FF", CultureInfo.InvariantCulture);

    public string? Resolve(HttpContext context)
    {
        var version = context.Features.Get<IApiVersioningFeature>()?.RequestedApiVersion;
        return version is null ? null : Key(version);
    }

    public IReadOnlyCollection<string> GetDeclaredVersions(Endpoint endpoint)
    {
        var metadata = endpoint.Metadata.GetMetadata<ApiVersionMetadata>();
        return metadata?.Map(ApiVersionMapping.Explicit | ApiVersionMapping.Implicit).DeclaredApiVersions
            .Select(Key).ToArray() ?? [];
    }

    public bool IsVersionNeutral(Endpoint endpoint) => endpoint.Metadata.GetMetadata<ApiVersionMetadata>()?.IsApiVersionNeutral == true;

    // Asp.Versioning 10.2 synthesizes empty, metadata-free RouteEndpoints at the
    // lowest matching priority. They are not real API routes. Match the shape
    // documented by its ClientErrorEndpoint factory, never a display name.
    public bool IsErrorEndpoint(Endpoint endpoint) => endpoint is RouteEndpoint route &&
        route.Order == int.MaxValue && route.RoutePattern.PathSegments.Count == 0 && endpoint.Metadata.Count == 0;
}
