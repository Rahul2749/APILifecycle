using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using ApiLifecycle.Testing;
using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Xunit;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace ApiLifecycle.Tests;

public sealed class LifecycleTests
{
    private static readonly DateTimeOffset Deprecation = DateTimeOffset.FromUnixTimeSeconds(1688169599);
    private static readonly DateTimeOffset Sunset = new(2024, 6, 30, 23, 59, 59, TimeSpan.Zero);
    private static ApiVersionPolicy Legacy => new()
    {
        Version = "1.0", DeprecationAt = Deprecation, SunsetAt = Sunset,
        SuccessorVersion = "2.0", SuccessorUrl = new Uri("https://example.com/v2"),
        PolicyUrl = new Uri("https://example.com/deprecation")
    };

    [Fact]
    public async Task HeadersMatchRfcFormatsAndPreserveExistingLinks()
    {
        await using var app = await Start(Legacy, Deprecation.AddDays(-1), map: app =>
            app.MapGet("/orders/{id}", (HttpContext context) =>
            {
                context.Response.Headers.Append("Link", "<https://example.com/help>; rel=\"help\"");
                return "ok";
            }).WithApiLifecycle("1.0"));
        using var response = await app.GetTestClient().GetAsync("/orders/42", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("@1688169599", Assert.Single(response.Headers.GetValues("Deprecation")));
        Assert.Equal("Sun, 30 Jun 2024 23:59:59 GMT", Assert.Single(response.Headers.GetValues("Sunset")));
        var links = string.Join(",", response.Headers.GetValues("Link"));
        Assert.Contains("rel=\"help\"", links);
        Assert.Contains("<https://example.com/v2>; rel=\"successor-version\"", links);
        Assert.Contains("rel=\"deprecation\"", links);
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK)]
    [InlineData(0, HttpStatusCode.Gone)]
    [InlineData(1, HttpStatusCode.Gone)]
    public async Task RetirementBoundaryIsInclusiveAndSkipsHandler(int seconds, HttpStatusCode expected)
    {
        var called = false;
        await using var app = await Start(Legacy, Sunset.AddSeconds(seconds), map: app =>
            app.MapGet("/orders", () => { called = true; return "ok"; }).WithApiLifecycle("1.0"));
        using var response = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK, called);
        if (expected == HttpStatusCode.Gone)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(410, body.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("2.0", body.RootElement.GetProperty("successorVersion").GetString());
        }
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK)]
    [InlineData(0, HttpStatusCode.ServiceUnavailable)]
    [InlineData(9, HttpStatusCode.ServiceUnavailable)]
    [InlineData(10, HttpStatusCode.OK)]
    public async Task BrownoutUsesHalfOpenInterval(int seconds, HttpStatusCode expected)
    {
        var start = Deprecation.AddDays(1);
        var policy = Legacy with { Brownouts = [new(start, start.AddSeconds(10))] };
        await using var app = await Start(policy, start.AddSeconds(seconds));
        using var response = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.ServiceUnavailable)
        {
            Assert.Equal(TimeSpan.FromSeconds(10 - seconds), response.Headers.RetryAfter?.Delta);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task ClockChangesApplyWithoutRebuildingRegistry()
    {
        var clock = new ManualTime(Deprecation.AddSeconds(-1));
        await using var app = await Start(Legacy, clock.Now, services: services => services.AddSingleton<TimeProvider>(clock));
        var registry = app.Services.GetRequiredService<IApiLifecycleRegistry>();
        LifecycleAssertions.HasState(registry, "1.0", LifecycleState.Active, clock);
        clock.Now = Deprecation;
        LifecycleAssertions.HasState(registry, "1.0", LifecycleState.Deprecated, clock);
        clock.Now = Sunset;
        LifecycleAssertions.HasState(registry, "1.0", LifecycleState.Retired, clock);
        using var response = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task RetirementCanBeAdvisory()
    {
        await using var app = await Start(Legacy, Sunset, configure: options => options.EnforceRetirement = false);
        using var response = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task ActiveUnmatchedAndExemptEndpointsPassThrough()
    {
        await using var app = await Start(new ApiVersionPolicy { Version = "1.0" }, Sunset, map: app =>
        {
            app.MapGet("/orders", () => "ok").WithApiLifecycle("1.0");
            app.MapGet("/health", () => "ok").SkipApiLifecycle();
        });
        using var active = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        using var exempt = await app.GetTestClient().GetAsync("/health", TestContext.Current.CancellationToken);
        using var missing = await app.GetTestClient().GetAsync("/missing", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.False(active.Headers.Contains("Deprecation"));
        Assert.Equal(HttpStatusCode.OK, exempt.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task StrictStartupRejectsUnversionedEndpoint()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start(Legacy, Deprecation,
            map: app => app.MapGet("/forgotten", () => "ok")));
    }

    [Fact]
    public async Task StrictStartupRejectsUnregisteredVersion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start(Legacy, Deprecation,
            map: app => app.MapGet("/forgotten", () => "ok").WithApiLifecycle("9.0")));
    }

    public static IEnumerable<object[]> InvalidPolicies()
    {
        yield return [Legacy with { SunsetAt = Deprecation.AddSeconds(-1) }];
        yield return [Legacy with { SuccessorVersion = "missing" }];
        yield return [Legacy with { SuccessorVersion = "1.0" }];
        yield return [Legacy with { PolicyUrl = new Uri("/relative", UriKind.Relative) }];
        yield return [Legacy with { SuccessorUrl = new Uri("javascript:alert(1)") }];
        yield return [Legacy with { State = (LifecycleState)999 }];
        yield return [Legacy with { State = LifecycleState.Deprecated, DeprecationAt = null }];
        yield return [Legacy with { State = LifecycleState.Sunset, SunsetAt = null }];
        yield return [Legacy with { Brownouts = [new(Deprecation, Deprecation)] }];
        yield return [Legacy with { Brownouts = [new(Deprecation.AddSeconds(-1), Deprecation.AddSeconds(1))] }];
        yield return [Legacy with { Brownouts = [new(Sunset, Sunset.AddMinutes(1))] }];
        yield return [Legacy with { Brownouts = [new(Deprecation, Deprecation.AddHours(2)), new(Deprecation.AddHours(1), Deprecation.AddHours(3))] }];
    }

    [Theory]
    [MemberData(nameof(InvalidPolicies))]
    public async Task InvalidPolicyFailsAtHostStartup(ApiVersionPolicy policy)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => Start(policy, Deprecation));
    }

    [Fact]
    public async Task DuplicateVersionsAndSuccessorCyclesFailStartup()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => Start(Legacy, Deprecation,
            configure: options => options.Versions.Add(Legacy)));
        await Assert.ThrowsAsync<OptionsValidationException>(() => Start(Legacy, Deprecation,
            configure: options => options.Versions[1] = options.Versions[1] with { SuccessorVersion = "1.0" }));
    }

    [Fact]
    public async Task RegistrySnapshotsMutableBrownoutCollection()
    {
        var windows = new List<BrownoutWindow>();
        await using var app = await Start(Legacy with { Brownouts = windows }, Deprecation.AddDays(1));
        windows.Add(new(Deprecation, Sunset));
        using var response = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConfigurationBindingCreatesWorkingPolicy()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiLifecycle:StrictMode"] = "true",
            ["ApiLifecycle:Versions:0:Version"] = "1.0",
            ["ApiLifecycle:Versions:0:State"] = "Retired"
        }).Build();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiLifecycle(configuration.GetSection("ApiLifecycle"));
        await using var app = builder.Build();
        app.UseRouting();
        app.UseApiLifecycle();
        app.MapGet("/orders", () => "ok").WithApiLifecycle("1.0");
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var response = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task AdapterReadsSelectedVersionAndRejectsOnlyRetiredVersion()
    {
        await using var app = await Start(Legacy, Sunset, versioning: true, map: app =>
        {
            var api = app.NewVersionedApi("Orders");
            api.MapGroup("/v{version:apiVersion}").HasApiVersion(1.0).MapGet("/orders", () => "old");
            api.MapGroup("/v{version:apiVersion}").HasApiVersion(2.0).MapGet("/orders", () => "new");
        });
        LifecycleAssertions.AllEndpointsHavePolicies(app.Services.GetRequiredService<EndpointDataSource>(),
            app.Services.GetRequiredService<IApiLifecycleVersionResolver>(), app.Services.GetRequiredService<IApiLifecycleRegistry>());
        using var old = await app.GetTestClient().GetAsync("/v1/orders", TestContext.Current.CancellationToken);
        using var current = await app.GetTestClient().GetAsync("/v2/orders", TestContext.Current.CancellationToken);
        using var unsupported = await app.GetTestClient().GetAsync("/v9/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Gone, old.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal("new", await current.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(unsupported.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task MetricsUseRouteTemplatesAndBoundedHashedClientTags()
    {
        var measurements = new ConcurrentQueue<Dictionary<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ApiLifecycleMetrics.MeterName && instrument.Name == "api.lifecycle.requests")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            Assert.Equal(1, value);
            measurements.Enqueue(tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value));
        });
        listener.Start();
        await using var app = await Start(Legacy, Sunset, configure: options =>
        {
            options.ClientIdentifier = context => context.Request.Headers["X-Client"].ToString();
            options.MaxTrackedClients = 1;
        }, map: app => app.MapGet("/orders/{id}", () => "ok").WithApiLifecycle("1.0"));
        var client = app.GetTestClient();
        foreach (var id in new[] { "client-a", "client-b", "client-a", "" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/secret-path");
            request.Headers.Add("X-Client", id);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        }
        var recorded = measurements.ToArray();
        Assert.Equal(4, recorded.Length);
        var hashed = Assert.IsType<string>(recorded[0]["client.id"]);
        Assert.Equal(64, hashed.Length);
        Assert.DoesNotContain("client-a", hashed);
        Assert.Equal("other", recorded[1]["client.id"]);
        Assert.Equal(hashed, recorded[2]["client.id"]);
        Assert.Equal("unknown", recorded[3]["client.id"]);
        Assert.All(recorded, tags =>
        {
            Assert.Equal("/orders/{id}", tags["http.route"]);
            Assert.Equal("retired", tags["api.lifecycle.outcome"]);
            Assert.Equal(410, tags["http.response.status_code"]);
        });
    }

    [Fact]
    public void ActiveRequestWithoutMetricsListenerStaysWithinAllocationBudget()
    {
        var options = Options.Create(new ApiLifecycleOptions
        {
            Versions = [new ApiVersionPolicy { Version = "1.0" }]
        });
        var registry = new ApiLifecycleRegistry(options);
        using var metrics = new ApiLifecycleMetrics(options);
        var middleware = new ApiLifecycleMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ApiLifecycleVersionMetadata("1.0")), "active"));
        var resolver = new MetadataVersionResolver();
        void Invoke() => middleware.InvokeAsync(context, registry, resolver, options, TimeProvider.System, metrics).GetAwaiter().GetResult();
        for (var i = 0; i < 1000; i++) Invoke();
        var before = GC.GetAllocatedBytesForCurrentThread();
        const int count = 10000;
        for (var i = 0; i < count; i++) Invoke();
        var bytesPerRequest = (GC.GetAllocatedBytesForCurrentThread() - before) / count;
        // Middleware only: excludes hosting, endpoint execution, exporters, and opt-in client hashing.
        Assert.True(bytesPerRequest <= 256, $"Active path allocated {bytesPerRequest} B/request (budget 256).");
    }

    [Fact]
    public async Task AdapterUsesAssumedDefaultAndExemptsNeutralEndpoints()
    {
        await using var app = await Start(Legacy, Sunset, versioning: true,
            services: services => services.Configure<ApiVersioningOptions>(options =>
            {
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ApiVersionReader = new QueryStringApiVersionReader();
            }), map: app =>
            {
                var api = app.NewVersionedApi("Orders");
                api.MapGet("/orders", () => "old").HasApiVersion(1.0);
                api.MapGet("/neutral", () => "ok").IsApiVersionNeutral();
            });
        using var old = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        using var neutral = await app.GetTestClient().GetAsync("/neutral", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Gone, old.StatusCode);
        Assert.Equal(HttpStatusCode.OK, neutral.StatusCode);
        Assert.False(neutral.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task EscapingExceptionDoesNotReportDefaultSuccessStatus()
    {
        var measurements = new ConcurrentQueue<Dictionary<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ApiLifecycleMetrics.MeterName && instrument.Name == "api.lifecycle.requests")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            measurements.Enqueue(tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
        listener.Start();
        await using var app = await Start(new ApiVersionPolicy { Version = "1.0" }, Deprecation,
            map: app => app.MapGet("/orders", (Func<string>)(() => throw new InvalidOperationException("endpoint failure"))).WithApiLifecycle("1.0"),
            beforeLifecycle: app => app.Use(async (context, next) =>
            {
                try { await next(context); }
                catch (InvalidOperationException) { context.Response.StatusCode = StatusCodes.Status409Conflict; }
            }));
        using var response = await app.GetTestClient().GetAsync("/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var tags = Assert.Single(measurements);
        Assert.Equal("exception", tags["api.lifecycle.outcome"]);
        Assert.False(tags.ContainsKey("http.response.status_code"));
    }

    [Fact]
    public void ExceptionMetricsPreserveStatusWhenResponseHasStarted()
    {
        var measurements = new List<Dictionary<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ApiLifecycleMetrics.MeterName && instrument.Name == "api.lifecycle.requests")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            measurements.Add(tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
        listener.Start();
        using var metrics = new ApiLifecycleMetrics(Options.Create(new ApiLifecycleOptions()));
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponse());
        metrics.Record(context, "1.0", LifecycleState.Active, "exception", null, System.Diagnostics.Stopwatch.GetTimestamp());
        Assert.Equal(202, Assert.Single(measurements)["http.response.status_code"]);
    }

    private sealed class StartedResponse : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
    {
        public StartedResponse() => StatusCode = StatusCodes.Status202Accepted;
        public override bool HasStarted => true;
    }

    private static async Task<WebApplication> Start(ApiVersionPolicy policy, DateTimeOffset now,
        Action<ApiLifecycleOptions>? configure = null, Action<WebApplication>? map = null,
        Action<IServiceCollection>? services = null, bool versioning = false, Action<WebApplication>? beforeLifecycle = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(new ManualTime(now));
        builder.Services.AddApiLifecycle(options =>
        {
            options.StrictMode = true;
            options.Versions.Add(policy);
            options.Versions.Add(new ApiVersionPolicy { Version = "2.0" });
            configure?.Invoke(options);
        });
        if (versioning)
        {
            builder.Services.AddApiVersioning(options => options.ApiVersionReader = new UrlSegmentApiVersionReader());
            builder.Services.AddApiLifecycleVersioning();
        }
        services?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseRouting();
        beforeLifecycle?.Invoke(app);
        app.UseApiLifecycle();
        if (map is not null) map(app);
        else app.MapGet("/orders", () => "ok").WithApiLifecycle("1.0");
        try { await app.StartAsync(TestContext.Current.CancellationToken); return app; }
        catch { await app.DisposeAsync(); throw; }
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
