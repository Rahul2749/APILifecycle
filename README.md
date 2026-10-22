# ApiLifecycle

An early .NET 10 NuGet package family for lifecycle governance on top of ASP.NET Core versioning. Version `0.0.1-preview.1` is an experimental prerelease; public APIs may change before 1.0.

Install the core package, or use the optional adapter when your application uses Asp.Versioning:

```powershell
dotnet add package ApiLifecycle.AspNetCore --version 0.0.1-preview.1
# Alternatively, the adapter includes the core packages transitively:
dotnet add package ApiLifecycle.Versioning --version 0.0.1-preview.1
```

| Package | Purpose |
| --- | --- |
| ApiLifecycle.Abstractions | Immutable policy types, states, brownout windows, registry interface; no package dependencies |
| ApiLifecycle.AspNetCore | Startup validation, endpoint metadata, headers, enforcement, BCL metrics; shared framework only |
| ApiLifecycle.Versioning | Optional Asp.Versioning.Http 10.2.3 adapter |
| ApiLifecycle.Testing | Test framework independent assertions for state and endpoint coverage |

## Quick start

```csharp
using ApiLifecycle;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApiLifecycle(options =>
{
    options.StrictMode = true;
    options.Versions.Add(new ApiVersionPolicy
    {
        Version = "1.0",
        DeprecationAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
        SunsetAt = DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
        SuccessorVersion = "2.0",
        SuccessorUrl = new Uri("https://api.example.com/v2/orders"),
        PolicyUrl = new Uri("https://api.example.com/lifecycle")
    });
    options.Versions.Add(new ApiVersionPolicy { Version = "2.0" });
});
var app = builder.Build();
app.UseRouting();
// Put authentication and authorization here when used by the application.
app.UseApiLifecycle();
app.MapGet("/v1/orders", () => new[] { "order-1" }).WithApiLifecycle("1.0");
app.MapGet("/v2/orders", () => new[] { "order-1", "order-2" }).WithApiLifecycle("2.0");
app.MapGet("/health", () => "healthy").SkipApiLifecycle();
app.Run();
```

For configuration binding, use `AddApiLifecycle(builder.Configuration.GetSection("ApiLifecycle"))`. The section has `StrictMode`, `EnforceRetirement`, `MaxTrackedClients`, and a `Versions` array matching the policy model. Configure a client identifier delegate separately in code.

For Asp.Versioning, register `AddApiVersioning(...)`, `AddApiLifecycle(...)`, and `AddApiLifecycleVersioning()`. The adapter reads `IApiVersioningFeature.RequestedApiVersion` after route selection, including assumed defaults; it does not parse caller input. Use canonical version strings (`"1.0"`, not `"v1"`). Minimal API groups and MVC controller metadata use the same adapter. The runnable minimal API sample demonstrates the integration:

```powershell
dotnet run --project samples/ApiLifecycle.Sample --urls http://localhost:5080
curl.exe -i http://localhost:5080/api/v1/orders
curl.exe -i http://localhost:5080/api/v2/orders
```

Run middleware after `UseRouting` and authorization. Authorization should complete before any lifecycle rejection. Unmatched routes and explicitly skipped/version-neutral endpoints pass through. In strict mode, all routed endpoints need policies or exemptions, including health checks and documentation routes. Endpoint coverage is checked at startup; dynamic endpoints added later are checked when requested.

## Policy behavior

Dates are instants (`DateTimeOffset`); comparisons use the injected `TimeProvider`. Policies are snapshotted at startup; configuration reload is not supported in this preview.

- `State` is an explicit baseline, defaulting to `Active`; `Preview` requires an explicit choice.
- At `DeprecationAt`, preview/active policies become `Deprecated`. Deprecation alone never rejects requests.
- `Sunset` is an explicitly chosen announced phase and requires both dates. At `SunsetAt` (inclusive), **any** policy becomes `Retired` and returns 410. `Retired` can also be explicitly selected for immediate retirement.
- `EnforceRetirement = false` makes retirement advisory; headers and metrics remain enabled. Brownouts still apply before retirement.
- Brownouts are explicit intervals `[Start, End)`, return 503 with `Retry-After` seconds and `Cache-Control: no-store`, and must lie on/after deprecation and end by sunset when configured. Recurring schedules are deferred.
- Rejections use `application/problem+json`, include the version and successor version when configured, and never execute the endpoint handler.
- Future dates are advertised immediately: `Deprecation: @<unix-seconds>`, `Sunset: <RFC 1123 GMT date>`, and HTTP `Link` values. Existing Link values are preserved. ApiLifecycle owns Deprecation/Sunset values on tracked responses; avoid configuring conflicting policies in Asp.Versioning.
- Successor version references are validated, but a `successor-version` link requires an explicit `SuccessorUrl`; the library cannot infer public URLs from version names.

Dates, duplicate keys, successor references/cycles, URI safety, brownout overlap, and client caps are validated at host startup. The key comparison is ordinal and case sensitive.

## Metrics

Subscribe to the BCL meter `ApiLifecycle` using your existing metrics exporter (for example, OpenTelemetry `AddMeter("ApiLifecycle")`). The library does not require an OpenTelemetry dependency.

Instruments: `api.lifecycle.requests` (counter, requests) and `api.lifecycle.request.duration` (histogram, seconds). Tags: `api.version`, `api.lifecycle.state`, `http.route` (route template, never raw request path), `http.response.status_code`, and `api.lifecycle.outcome` (`served`, `retired`, `brownout`, `exception`). If an exception escapes before response headers start, the status tag is omitted because an outer exception handler can still choose it. Duration covers downstream execution until the middleware returns, not transport completion.

Client identity is disabled by default. Enable with `options.ClientIdentifier = context => context.User.FindFirst("client_id")?.Value`. Only return a trusted client name or claim; never a raw secret. IDs use HMAC-SHA256 with a random per-host key, so they are not stable across restarts or instances. At most `MaxTrackedClients` hashes (default 100, maximum 10,000) are retained per host. Additional clients use `other`; missing clients use `unknown`. This preview uses first-seen buckets, **not top-N usage ranking**. Hashing and its allocations apply only when tracking is enabled.

No report/admin endpoints or identity store are installed. Usage export, cross-host identity correlation, and top-N reports remain future work.

## Development

```powershell
dotnet restore ApiLifecycle.slnx
dotnet build ApiLifecycle.slnx -c Release --no-restore
dotnet test --solution ApiLifecycle.slnx -c Release --no-build
dotnet pack ApiLifecycle.slnx -c Release --no-build -o artifacts/packages
```

CI gates each package at 150 KiB and the warmed active middleware path at 256 B/request without metrics listeners or client tracking. The allocation check excludes hosting and endpoint execution; it is not an end-to-end throughput benchmark.

See [the plan review](https://github.com/Rahul2749/APILifecycle/blob/main/docs/plan-review.md) and [lifecycle contract](https://github.com/Rahul2749/APILifecycle/blob/main/docs/lifecycle-model.md) for decisions and remaining work. Contract diff tooling, usage reports, OpenAPI policy projection, and MVC integration coverage are deferred. This preview supports .NET 10 only.

Licensed under [MIT](https://github.com/Rahul2749/APILifecycle/blob/main/LICENSE). Report issues at [GitHub Issues](https://github.com/Rahul2749/APILifecycle/issues).
