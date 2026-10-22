using ApiLifecycle;
using Asp.Versioning;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApiVersioning(options => options.ApiVersionReader = new UrlSegmentApiVersionReader());
builder.Services.AddApiLifecycle(options =>
{
    options.StrictMode = true;
    options.Versions.Add(new ApiVersionPolicy
    {
        Version = "1.0",
        DeprecationAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        SunsetAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
        SuccessorVersion = "2.0",
        SuccessorUrl = new Uri("https://api.example.com/api/v2/orders"),
        PolicyUrl = new Uri("https://api.example.com/lifecycle")
    });
    options.Versions.Add(new ApiVersionPolicy { Version = "2.0" });
});
builder.Services.AddApiLifecycleVersioning();

var app = builder.Build();
app.UseRouting();
app.UseApiLifecycle();
var api = app.NewVersionedApi("Orders");
var v1 = api.MapGroup("/api/v{version:apiVersion}").HasApiVersion(1.0);
var v2 = api.MapGroup("/api/v{version:apiVersion}").HasApiVersion(2.0);
v1.MapGet("/orders", () => new[] { new { Id = 1, Description = "Legacy order" } });
v2.MapGet("/orders", () => new[] { new { Id = 1, Description = "Current order", Status = "ready" } });
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).SkipApiLifecycle();
app.Run();
