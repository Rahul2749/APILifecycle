using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ApiLifecycle;

public sealed class ApiLifecycleMetrics : IDisposable
{
    public const string MeterName = "ApiLifecycle";
    private readonly Meter meter = new(MeterName, typeof(ApiLifecycleMetrics).Assembly.GetName().Version?.ToString(3));
    private readonly Counter<long> requests;
    private readonly Histogram<double> duration;
    private readonly ApiLifecycleOptions options;
    private readonly HashSet<string> clients = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly byte[] hashKey = RandomNumberGenerator.GetBytes(32);

    public ApiLifecycleMetrics(IOptions<ApiLifecycleOptions> options)
    {
        this.options = options.Value;
        requests = meter.CreateCounter<long>("api.lifecycle.requests", "{request}");
        duration = meter.CreateHistogram<double>("api.lifecycle.request.duration", "s");
    }

    internal string? GetClient(HttpContext context)
    {
        if (options.ClientIdentifier is null) return null;
        var identity = options.ClientIdentifier(context);
        if (string.IsNullOrWhiteSpace(identity)) return "unknown";
        // Keyed hashing prevents offline guessing; raw identities are never retained.
        if (identity.Length > 1024) return "other";
        var hash = Convert.ToHexString(HMACSHA256.HashData(hashKey, Encoding.UTF8.GetBytes(identity)));
        lock (gate)
        {
            if (clients.Contains(hash)) return hash;
            if (clients.Count >= options.MaxTrackedClients) return "other";
            clients.Add(hash);
            return hash;
        }
    }

    internal void Record(HttpContext context, string version, LifecycleState state, string outcome, string? client, long start)
    {
        if (!requests.Enabled && !duration.Enabled) return;
        TagList tags = new()
        {
            { "api.version", version },
            { "api.lifecycle.state", state.ToString() },
            { "http.route", (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown" },
            { "api.lifecycle.outcome", outcome }
        };
        // An outer exception handler may still choose the final status. Do not
        // report the default 200 for a response whose headers have not started.
        if (outcome != "exception" || context.Response.HasStarted)
            tags.Add("http.response.status_code", context.Response.StatusCode);
        if (client is not null) tags.Add("client.id", client);
        requests.Add(1, tags);
        duration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
    }

    public void Dispose() => meter.Dispose();
}
