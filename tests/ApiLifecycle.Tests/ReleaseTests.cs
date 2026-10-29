using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ApiLifecycle.Testing;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApiLifecycle.Tests;

public sealed partial class LifecycleTests
{
    [Fact]
    public async Task MethodMismatchRetains405InStrictMode()
    {
        await using var app = await Start(Legacy, Deprecation);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("?api-version=garbage")]
    [InlineData("?api-version=99.0")]
    [InlineData("?api-version=1.0&api-version=2.0")]
    [InlineData("")]
    public async Task VersioningErrorsRetain400InStrictMode(string query)
    {
        await using var app = await Start(Legacy, Deprecation, versioning: true,
            services: services => services.Configure<ApiVersioningOptions>(options => options.ApiVersionReader = new QueryStringApiVersionReader()),
            map: app => app.NewVersionedApi("Errors").MapGet("/orders", () => "ok").HasApiVersion(1.0));
        using var response = await app.GetTestClient().GetAsync("/orders" + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Sunset"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]
    public async Task AdapterMatchesEquivalentNumericVersionKeys(string key)
    {
        await using var app = await Start(new ApiVersionPolicy { Version = key, State = LifecycleState.Retired }, Sunset,
            versioning: true, map: app => app.NewVersionedApi("Alias").MapGroup("/v{version:apiVersion}")
                .HasApiVersion(1.0).MapGet("/orders", () => "never"));
        using var response = await app.GetTestClient().GetAsync("/v1/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task AdapterRejectsEquivalentDuplicateKeysEvenInPermissiveMode()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => Start(Legacy, Deprecation, versioning: true,
            configure: options => { options.StrictMode = false; options.Versions.Add(new ApiVersionPolicy { Version = "1" }); }));
    }

    [Fact]
    public async Task AdapterRejectsInvalidVersionKeyEvenInPermissiveMode()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => Start(Legacy with { Version = "invalid-version" }, Deprecation,
            versioning: true, configure: options => options.StrictMode = false));
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-01")]
    [InlineData("1-beta", "1.0-beta")]
    [InlineData("1.0-BETA", "1.0-beta")]
    public async Task AdapterPreservesDateAndStatusVersionSemantics(string key, string requested)
    {
        await using var app = await Start(new ApiVersionPolicy { Version = key, State = LifecycleState.Retired }, Sunset,
            versioning: true,
            services: services => services.Configure<ApiVersioningOptions>(options => options.ApiVersionReader = new QueryStringApiVersionReader()),
            map: app => app.NewVersionedApi("Semantic").MapGet("/orders", () => "never")
                .HasApiVersion(ApiVersionParser.Default.Parse(key.AsSpan())));
        using var response = await app.GetTestClient().GetAsync("/orders?api-version=" + requested, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Theory]
    [InlineData("versions")]
    [InlineData("policy")]
    [InlineData("brownouts")]
    [InlineData("window")]
    public async Task NullConfigurationFailsWithActionableOptionsValidation(string part)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => Start(Legacy, Deprecation, configure: options =>
        {
            switch (part)
            {
                case "versions": options.Versions = null!; break;
                case "policy": options.Versions.Add(null!); break;
                case "brownouts": options.Versions[0] = Legacy with { Brownouts = null! }; break;
                case "window": options.Versions[0] = Legacy with { Brownouts = new BrownoutWindow[] { null! } }; break;
            }
        }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public async Task InvalidClientCapFailsStartup(int cap)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => Start(Legacy, Deprecation,
            configure: options => options.MaxTrackedClients = cap));
    }

    [Theory]
    [InlineData("query")]
    [InlineData("header")]
    [InlineData("media")]
    public async Task MvcControllerResolvesVersionAndEnforcesRetirement(string reader)
    {
        await using var app = await Start(Legacy, Sunset, versioning: true, services: services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ReleaseOrdersController).Assembly);
            services.AddApiVersioning().AddMvc();
            services.Configure<ApiVersioningOptions>(options => options.ApiVersionReader = reader switch
            {
                "header" => new HeaderApiVersionReader("X-Api-Version"),
                "media" => new MediaTypeApiVersionReader("v"),
                _ => new QueryStringApiVersionReader()
            });
        }, map: app => app.MapControllers());
        var client = app.GetTestClient();
        foreach (var version in new[] { "1.0", "2.0" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "/release/orders" + (reader == "query" ? "?api-version=" + version : ""));
            if (reader == "header") request.Headers.Add("X-Api-Version", version);
            if (reader == "media") request.Headers.Accept.ParseAdd("application/json;v=" + version);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(version == "1.0" ? HttpStatusCode.Gone : HttpStatusCode.OK, response.StatusCode);
        }
        LifecycleAssertions.AllEndpointsHavePolicies(app.Services.GetRequiredService<EndpointDataSource>(),
            app.Services.GetRequiredService<IApiLifecycleVersionResolver>(), app.Services.GetRequiredService<IApiLifecycleRegistry>());
    }

    [Fact]
    public async Task AuthorizationCompletesBeforeRetirementEnforcement()
    {
        await using var app = await Start(Legacy, Sunset, services: services =>
        {
            services.AddAuthentication("release-test").AddScheme<AuthenticationSchemeOptions, ReleaseAuthenticationHandler>("release-test", _ => { });
            services.AddAuthorization();
        }, beforeLifecycle: app => { app.UseAuthentication(); app.UseAuthorization(); },
            map: app => app.MapGet("/orders", () => "never").WithApiLifecycle("1.0").RequireAuthorization());
        using var anonymous = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.False(anonymous.Headers.Contains("Sunset"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
        request.Headers.Add("X-Release-Test-User", "authenticated");
        using var authenticated = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Gone, authenticated.StatusCode);
    }

    [Fact]
    public void ConcurrentClientIdentifiersCannotExceedCap()
    {
        using var metrics = new ApiLifecycleMetrics(Options.Create(new ApiLifecycleOptions
        {
            MaxTrackedClients = 8, ClientIdentifier = context => context.Request.Headers["X-Client"].ToString()
        }));
        var buckets = new ConcurrentBag<string>();
        Parallel.For(0, 1000, i =>
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Client"] = "client-" + i;
            buckets.Add(metrics.GetClient(context)!);
        });
        var hashes = buckets.Where(bucket => bucket != "other").Distinct().ToArray();
        Assert.Equal(8, hashes.Length);
        Assert.All(hashes, hash => Assert.Equal(64, hash.Length));
        Assert.Contains("other", buckets);
    }
}

[ApiController]
[Route("release/orders")]
[ApiVersion(1.0)]
[ApiVersion(2.0)]
public sealed class ReleaseOrdersController : ControllerBase
{
    [HttpGet, MapToApiVersion(1.0)] public string Legacy() => "legacy";
    [HttpGet, MapToApiVersion(2.0)] public string Current() => "current";
    [HttpGet("shared")] public string Shared() => "shared";
}

internal sealed class ReleaseAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
        Request.Headers["X-Release-Test-User"] == "authenticated"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "release-test")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
}
