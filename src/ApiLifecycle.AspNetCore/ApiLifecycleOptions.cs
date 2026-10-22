using Microsoft.AspNetCore.Http;

namespace ApiLifecycle;

public sealed class ApiLifecycleOptions
{
    public List<ApiVersionPolicy> Versions { get; set; } = [];
    /// <summary>Reject startup if a routed endpoint lacks lifecycle/versioning metadata or an exemption.</summary>
    public bool StrictMode { get; set; }
    public bool EnforceRetirement { get; set; } = true;
    /// <summary>Opt-in; use a trusted identity claim, never a raw API key.</summary>
    public Func<HttpContext, string?>? ClientIdentifier { get; set; }
    public int MaxTrackedClients { get; set; } = 100;
}
