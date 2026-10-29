using ApiLifecycle;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddApiVersioning(options => options.ApiVersionReader = new QueryStringApiVersionReader()).AddMvc();
builder.Services.AddApiLifecycle(options =>
{
    options.StrictMode = true;
    options.Versions.Add(new ApiVersionPolicy { Version = "1.0", State = LifecycleState.Retired, SuccessorVersion = "2.0" });
    options.Versions.Add(new ApiVersionPolicy { Version = "2.0" });
});
builder.Services.AddApiLifecycleVersioning();
var app = builder.Build();
app.UseRouting();
app.UseApiLifecycle();
app.MapControllers();
app.Run();

[ApiController]
[Route("orders")]
[ApiVersion(1.0)]
[ApiVersion(2.0)]
public sealed class OrdersController : ControllerBase
{
    [HttpGet, MapToApiVersion(1.0)] public object Legacy() => new { message = "retired" };
    [HttpGet, MapToApiVersion(2.0)] public object Current() => new { id = 1, status = "ready" };
}
