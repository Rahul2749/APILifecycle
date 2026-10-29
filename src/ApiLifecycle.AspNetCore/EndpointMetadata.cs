using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ApiLifecycle;

public sealed record ApiLifecycleVersionMetadata(string Version);
public sealed record SkipApiLifecycleMetadata;

public static class ApiLifecycleEndpointExtensions
{
    public static TBuilder WithApiLifecycle<TBuilder>(this TBuilder builder, string version)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        builder.Add(endpoint => endpoint.Metadata.Add(new ApiLifecycleVersionMetadata(version)));
        return builder;
    }

    public static TBuilder SkipApiLifecycle<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(new SkipApiLifecycleMetadata()));
        return builder;
    }
}

/// <summary>Resolves a selected endpoint's version, rather than trusting request input.</summary>
public interface IApiLifecycleVersionResolver
{
    string? Resolve(HttpContext context);
    IReadOnlyCollection<string> GetDeclaredVersions(Endpoint endpoint);
    bool IsVersionNeutral(Endpoint endpoint);
    /// <summary>Identifies synthetic versioning error endpoints that must retain their framework response.</summary>
    bool IsErrorEndpoint(Endpoint endpoint) => false;
}

internal sealed class MetadataVersionResolver : IApiLifecycleVersionResolver
{
    public string? Resolve(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ApiLifecycleVersionMetadata>()?.Version;

    public IReadOnlyCollection<string> GetDeclaredVersions(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ApiLifecycleVersionMetadata>() is { } metadata ? [metadata.Version] : [];

    public bool IsVersionNeutral(Endpoint endpoint) => false;
}
