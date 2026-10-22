using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ApiLifecycle.Testing;

/// <summary>Framework-independent assertions, usable from any test runner.</summary>
public static class LifecycleAssertions
{
    public static void HasState(IApiLifecycleRegistry registry, string version, LifecycleState expected, TimeProvider time)
    {
        if (!registry.TryGetPolicy(version, out var policy)) throw new InvalidOperationException($"No policy registered for '{version}'.");
        var actual = policy.GetState(time.GetUtcNow());
        if (actual != expected) throw new InvalidOperationException($"Version '{version}': expected {expected}, found {actual}.");
    }

    public static void AllEndpointsHavePolicies(EndpointDataSource source, IApiLifecycleVersionResolver resolver, IApiLifecycleRegistry registry)
    {
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<SkipApiLifecycleMetadata>() is not null || resolver.IsVersionNeutral(endpoint)) continue;
            var versions = resolver.GetDeclaredVersions(endpoint);
            if (versions.Count == 0 || versions.Any(version => !registry.TryGetPolicy(version, out _)))
                throw new InvalidOperationException($"Endpoint '{endpoint.DisplayName}' has missing lifecycle policies.");
        }
    }
}
