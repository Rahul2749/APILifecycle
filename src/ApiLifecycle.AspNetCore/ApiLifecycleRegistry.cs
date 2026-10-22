using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace ApiLifecycle;

internal sealed record CompiledPolicy(ApiVersionPolicy Policy, string? Deprecation, string? Sunset, string[] Links);

internal sealed class ApiLifecycleRegistry : IApiLifecycleRegistry
{
    private readonly FrozenDictionary<string, CompiledPolicy> policies;
    public IReadOnlyCollection<ApiVersionPolicy> Policies { get; }

    public ApiLifecycleRegistry(IOptions<ApiLifecycleOptions> options)
    {
        var errors = new List<string>();
        var source = options.Value;
        var versions = source.Versions;
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (source.MaxTrackedClients is < 0 or > 10000)
            errors.Add("MaxTrackedClients must be between 0 and 10000.");
        foreach (var policy in versions)
        {
            if (string.IsNullOrWhiteSpace(policy.Version) || !names.Add(policy.Version))
                errors.Add($"Version '{policy.Version}' must be nonempty and unique (ordinal comparison).");
        }
        foreach (var policy in versions)
        {
            void Error(string message) => errors.Add($"Version '{policy.Version}': {message}");
            if (!Enum.IsDefined(policy.State)) Error("unknown lifecycle state.");
            if (policy.State is LifecycleState.Deprecated or LifecycleState.Sunset && policy.DeprecationAt is null)
                Error("deprecated and sunset states require DeprecationAt.");
            if (policy.State == LifecycleState.Sunset && policy.SunsetAt is null)
                Error("sunset state requires SunsetAt.");
            if (policy.SunsetAt is { } sunset && policy.DeprecationAt is { } deprecated && sunset < deprecated)
                Error("SunsetAt must not precede DeprecationAt.");
            if (policy.SuccessorVersion is { } successor && (!names.Contains(successor) || successor == policy.Version))
                Error("SuccessorVersion must refer to a different registered version.");
            ValidateUrl(policy.SuccessorUrl, "SuccessorUrl", Error);
            ValidateUrl(policy.PolicyUrl, "PolicyUrl", Error);
            var windows = policy.Brownouts.OrderBy(window => window.Start).ToArray();
            for (var i = 0; i < windows.Length; i++)
            {
                var window = windows[i];
                if (window.End <= window.Start) Error("brownouts must have positive duration.");
                if (i > 0 && window.Start < windows[i - 1].End) Error("brownouts must not overlap.");
                if (policy.SunsetAt is { } cutoff && window.End > cutoff) Error("brownouts must end by SunsetAt.");
                if (policy.DeprecationAt is not { } start || window.Start < start)
                    Error("brownouts require DeprecationAt and must start on or after it.");
            }
        }
        // Catch cycles so successor chains remain useful to clients and future tooling.
        foreach (var policy in versions)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { policy.Version };
            var next = policy.SuccessorVersion;
            while (next is not null && names.Contains(next))
            {
                if (!visited.Add(next)) { errors.Add($"Successor cycle involving '{next}'."); break; }
                next = versions.First(p => p.Version == next).SuccessorVersion;
            }
        }
        if (errors.Count != 0) throw new OptionsValidationException(nameof(ApiLifecycleOptions), typeof(ApiLifecycleOptions), errors);

        var snapshots = versions.Select(policy => policy with
        {
            Brownouts = Array.AsReadOnly(policy.Brownouts.OrderBy(window => window.Start).ToArray())
        }).ToArray();
        Policies = Array.AsReadOnly(snapshots);
        policies = snapshots.ToFrozenDictionary(policy => policy.Version, Compile, StringComparer.Ordinal);
    }

    public bool TryGetPolicy(string version, out ApiVersionPolicy policy)
    {
        if (policies.TryGetValue(version, out var compiled)) { policy = compiled.Policy; return true; }
        policy = null!;
        return false;
    }

    internal bool TryGetCompiled(string version, out CompiledPolicy policy) => policies.TryGetValue(version, out policy!);

    private static CompiledPolicy Compile(ApiVersionPolicy policy)
    {
        var links = new List<string>(2);
        if (policy.SuccessorUrl is { } successor) links.Add($"<{successor.AbsoluteUri}>; rel=\"successor-version\"");
        if (policy.PolicyUrl is { } info) links.Add($"<{info.AbsoluteUri}>; rel=\"deprecation\"");
        return new(policy,
            policy.DeprecationAt is { } date ? "@" + date.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) : null,
            policy.SunsetAt?.UtcDateTime.ToString("R", CultureInfo.InvariantCulture), links.ToArray());
    }

    private static void ValidateUrl(Uri? url, string name, Action<string> error)
    {
        if (url is not null && (!url.IsAbsoluteUri || url.Scheme is not ("https" or "http") ||
            url.OriginalString.Any(c => char.IsControl(c) || c is '<' or '>' or '"')))
            error($"{name} must be an absolute HTTP(S) URI without unsafe header characters.");
    }
}
