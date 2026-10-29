using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ApiLifecycle;

internal sealed class ApiLifecycleMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApiLifecycleRegistry registry,
        IApiLifecycleVersionResolver resolver, IOptions<ApiLifecycleOptions> options,
        TimeProvider time, ApiLifecycleMetrics metrics)
    {
        var endpoint = context.GetEndpoint();
        // Leave framework-generated 400/405 responses to their owning component,
        // before asking the resolver to read potentially ambiguous version input.
        if (endpoint is not RouteEndpoint || endpoint.Metadata.GetMetadata<SkipApiLifecycleMetadata>() is not null ||
            resolver.IsVersionNeutral(endpoint) || resolver.IsErrorEndpoint(endpoint))
        {
            await next(context);
            return;
        }
        var version = resolver.Resolve(context);
        if (version is null || !registry.TryGetCompiled(version, out var compiled))
        {
            if (options.Value.StrictMode)
                throw new InvalidOperationException($"Endpoint '{endpoint.DisplayName}' has no registered lifecycle policy for '{version}'.");
            await next(context);
            return;
        }

        var now = time.GetUtcNow();
        var policy = compiled.Policy;
        var state = policy.GetState(now);
        var start = Stopwatch.GetTimestamp();
        var client = metrics.GetClient(context);
        var outcome = "served";
        // Registered after routing: these values also survive downstream header writes.
        if (compiled.Deprecation is not null || compiled.Sunset is not null || compiled.Links.Length != 0)
            context.Response.OnStarting(() =>
            {
                if (compiled.Deprecation is not null) context.Response.Headers["Deprecation"] = compiled.Deprecation;
                if (compiled.Sunset is not null) context.Response.Headers["Sunset"] = compiled.Sunset;
                foreach (var link in compiled.Links) context.Response.Headers.Append("Link", link);
                return Task.CompletedTask;
            });
        try
        {
            if (options.Value.EnforceRetirement && state == LifecycleState.Retired)
            {
                outcome = "retired";
                await Reject(context, 410, "API version retired", "This API version is no longer available.", version, policy);
                return;
            }
            var brownout = policy.Brownouts.FirstOrDefault(window => window.Contains(now));
            if (state != LifecycleState.Retired && brownout is not null)
            {
                outcome = "brownout";
                context.Response.Headers.RetryAfter = Math.Ceiling((brownout.End - now).TotalSeconds).ToString(CultureInfo.InvariantCulture);
                context.Response.Headers.CacheControl = "no-store";
                await Reject(context, 503, "API version brownout", "This API version is temporarily unavailable during a scheduled retirement rehearsal.", version, policy);
                return;
            }
            await next(context);
        }
        catch { outcome = "exception"; throw; }
        finally { metrics.Record(context, version, state, outcome, client, start); }
    }

    private static Task Reject(HttpContext context, int status, string title, string detail, string version, ApiVersionPolicy policy)
    {
        context.Response.StatusCode = status;
        // Avoid the default heuristic caching of 410 when policies can change on redeployment.
        context.Response.Headers.CacheControl = "no-store";
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Type = "about:blank" };
        problem.Extensions["apiVersion"] = version;
        if (policy.SuccessorVersion is { } successor) problem.Extensions["successorVersion"] = successor;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
