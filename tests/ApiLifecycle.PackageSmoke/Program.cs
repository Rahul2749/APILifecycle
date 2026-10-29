using System.Net;
using System.Text.Json;
using ApiLifecycle;
using ApiLifecycle.Testing;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

var clock = new SmokeTime();
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSingleton<TimeProvider>(clock);
builder.Services.AddControllers();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(2, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = ApiVersionReader.Combine(new UrlSegmentApiVersionReader(),
        new QueryStringApiVersionReader(), new HeaderApiVersionReader("X-Api-Version"));
}).AddMvc();
builder.Services.AddApiLifecycle(options =>
{
    options.StrictMode = true;
    options.Versions.Add(new ApiVersionPolicy { Version = "1.0", State = LifecycleState.Retired, SuccessorVersion = "2.0" });
    options.Versions.Add(new ApiVersionPolicy { Version = "2.0" });
    options.Versions.Add(new ApiVersionPolicy
    {
        Version = "3.0", DeprecationAt = DateTimeOffset.FromUnixTimeSeconds(1688169599),
        SunsetAt = clock.GetUtcNow().AddDays(30), SuccessorVersion = "2.0",
        SuccessorUrl = new Uri("https://example.com/v2"), PolicyUrl = new Uri("https://example.com/policy")
    });
    options.Versions.Add(new ApiVersionPolicy
    {
        Version = "4.0", DeprecationAt = clock.GetUtcNow().AddDays(-1),
        Brownouts = [new(clock.GetUtcNow().AddMinutes(-5), clock.GetUtcNow().AddMinutes(5))]
    });
});
builder.Services.AddApiLifecycleVersioning();
await using var app = builder.Build();
app.UseRouting();
app.UseApiLifecycle();
var api = app.NewVersionedApi("Smoke");
foreach (var version in new[] { 1, 2, 3, 4 })
    api.MapGroup("/v{version:apiVersion}").HasApiVersion(new ApiVersion(version, 0))
        .MapMethods("/orders", ["GET", "HEAD"], () => "ok");
api.MapGet("/orders", () => "ok").HasApiVersion(1.0).HasApiVersion(2.0);
app.MapGet("/health", () => "healthy").SkipApiLifecycle();
app.MapControllers();
await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
var passed = 0;
async Task<HttpResponseMessage> Check(string path, HttpStatusCode status, HttpMethod? method = null, string? version = null)
{
    using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
    if (version is not null) request.Headers.Add("X-Api-Version", version);
    var response = await client.SendAsync(request);
    if (response.StatusCode != status)
        throw new InvalidOperationException($"{request.Method} {path}: expected {(int)status}, got {(int)response.StatusCode}. {await response.Content.ReadAsStringAsync()}");
    passed++;
    return response;
}

using (var gone = await Check("/v1/orders", HttpStatusCode.Gone))
{
    if (gone.Content.Headers.ContentType?.MediaType != "application/problem+json") throw new InvalidOperationException("Missing ProblemDetails media type");
    using var body = JsonDocument.Parse(await gone.Content.ReadAsStringAsync());
    if (body.RootElement.GetProperty("successorVersion").GetString() != "2.0") throw new InvalidOperationException("Missing successor");
}
using (await Check("/v2/orders", HttpStatusCode.OK)) { }
using (var deprecated = await Check("/v3/orders", HttpStatusCode.OK))
{
    if (deprecated.Headers.GetValues("Deprecation").Single() != "@1688169599") throw new InvalidOperationException("Invalid RFC 9745 date");
    if (!deprecated.Headers.Contains("Sunset") || !deprecated.Headers.Contains("Link")) throw new InvalidOperationException("Missing lifecycle headers");
}
using (var brownout = await Check("/v4/orders", HttpStatusCode.ServiceUnavailable))
{
    if (brownout.Headers.RetryAfter?.Delta != TimeSpan.FromMinutes(5)) throw new InvalidOperationException("Invalid Retry-After");
}
using (await Check("/orders?api-version=1.0", HttpStatusCode.Gone)) { }
using (await Check("/orders", HttpStatusCode.Gone, version: "1.0")) { }
using (await Check("/orders", HttpStatusCode.OK)) { }
using (await Check("/mvc/orders?api-version=1.0", HttpStatusCode.Gone)) { }
using (await Check("/mvc/orders?api-version=2.0", HttpStatusCode.OK)) { }
using (await Check("/health", HttpStatusCode.OK)) { }
using (await Check("/missing", HttpStatusCode.NotFound)) { }
using (await Check("/v2/orders", HttpStatusCode.MethodNotAllowed, HttpMethod.Post)) { }
using (await Check("/orders?api-version=garbage", HttpStatusCode.BadRequest)) { }
using (await Check("/orders?api-version=99.0", HttpStatusCode.BadRequest)) { }
using (await Check("/orders?api-version=1.0&api-version=2.0", HttpStatusCode.BadRequest)) { }
using (await Check("/v2/orders", HttpStatusCode.OK, HttpMethod.Head)) { }
using (var head = await Check("/v1/orders", HttpStatusCode.Gone, HttpMethod.Head))
{
    if ((await head.Content.ReadAsStringAsync()).Length != 0) throw new InvalidOperationException("HEAD response must have no body");
}
var batch = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => client.GetAsync("/v2/orders")));
try { if (batch.Any(response => response.StatusCode != HttpStatusCode.OK)) throw new InvalidOperationException("Concurrent request failed"); }
finally { foreach (var response in batch) response.Dispose(); }
LifecycleAssertions.HasState(app.Services.GetRequiredService<IApiLifecycleRegistry>(), "1.0", LifecycleState.Retired, clock);
LifecycleAssertions.AllEndpointsHavePolicies(app.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>(),
    app.Services.GetRequiredService<IApiLifecycleVersionResolver>(), app.Services.GetRequiredService<IApiLifecycleRegistry>());
await app.StopAsync();
Console.WriteLine($"PACKAGE SMOKE PASSED: {passed} HTTP checks, 100 concurrent requests, registry and endpoint assertions. Real Kestrel; NuGet package references only.");

public sealed class SmokeTime : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
}

[ApiController]
[Route("mvc/orders")]
[ApiVersion(1.0)]
[ApiVersion(2.0)]
public sealed class SmokeOrdersController : ControllerBase
{
    [HttpGet, MapToApiVersion(1.0)]
    public string Legacy() => "old";
    [HttpGet, MapToApiVersion(2.0)]
    public string Current() => "current";
}
