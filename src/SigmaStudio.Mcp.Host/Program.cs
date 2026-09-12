using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol.Server;
using SigmaStudio.Catalog;
using SigmaStudio.Core;
using SigmaStudio.Graph;
using SigmaStudio.Mcp.Host;

var builder = WebApplication.CreateBuilder(args);
var sigmaOptions = new SigmaStudioOptions();
builder.Configuration.GetSection("SigmaStudio").Bind(sigmaOptions);
if (sigmaOptions.AllowedProjectRoots.Length == 0 && !sigmaOptions.AllowArbitraryPaths)
    sigmaOptions.AllowedProjectRoots = [Directory.GetCurrentDirectory()];
builder.WebHost.UseUrls($"http://{sigmaOptions.Host}:{sigmaOptions.Port}");

builder.Services.AddSingleton(sigmaOptions);
builder.Services.AddSingleton<ProjectPathPolicy>();
builder.Services.AddSingleton<GraphValidator>();
builder.Services.AddSingleton(sp => new CatalogStore(Path.Combine(AppContext.BaseDirectory, "data", "catalog", "adau1701", "catalog.json")));
builder.Services.AddSingleton<ISigmaStudioAutomation>(sp => sigmaOptions.Backend.Equals("Bridge", StringComparison.OrdinalIgnoreCase)
    ? new NamedPipeSigmaStudioAutomation(sigmaOptions.BridgePipeName)
    : new InMemorySigmaStudioAutomation());
builder.Services.AddSingleton<SigmaRuntime>(sp =>
{
    var catalog = sp.GetRequiredService<CatalogStore>();
    return new SigmaRuntime(
        sp.GetRequiredService<ISigmaStudioAutomation>(),
        sp.GetRequiredService<ProjectPathPolicy>(),
        sp.GetRequiredService<GraphValidator>(),
        () => catalog.Blocks,
        sigmaOptions);
});

var mcp = builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<SigmaTools>()
    .WithResources<SigmaResources>();
if (sigmaOptions.EnableRawParameterAccess || sigmaOptions.EnableRawRegisterAccess) mcp.WithTools<AdvancedTools>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if (!string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Only loopback Host values are accepted.");
        return;
    }
    if (context.Request.Headers.TryGetValue("Origin", out var origin) && origin.Count > 0 && !IsLoopbackOrigin(origin[0]))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Only loopback origins are accepted.");
        return;
    }
    await next(context);
});
app.MapGet("/health", () => Results.Ok(new { ok = true, service = "SigmaStudio.Mcp.Host" }));
app.MapMcp("/mcp");
await app.RunAsync();

static bool IsLoopbackOrigin(string? value)
{
    return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
}
