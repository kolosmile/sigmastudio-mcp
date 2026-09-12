using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SigmaStudio.Core;

namespace SigmaStudio.Mcp.Host;

[McpServerResourceType]
public sealed class SigmaResources
{
    private readonly SigmaRuntime _runtime;

    public SigmaResources(SigmaRuntime runtime) => _runtime = runtime;

    [McpServerResource(UriTemplate = "sigmastudio://status", Name = "SigmaStudio status", MimeType = "application/json")]
    [Description("Current application and project status.")]
    public async Task<TextResourceContents> Status(CancellationToken cancellationToken)
    {
        var result = await _runtime.StatusAsync(cancellationToken);
        return JsonResource("sigmastudio://status", result);
    }

    [McpServerResource(UriTemplate = "sigmastudio://project/graph", Name = "SigmaStudio graph", MimeType = "application/json")]
    [Description("Current semantic DSP graph.")]
    public async Task<TextResourceContents> Graph(CancellationToken cancellationToken)
    {
        var result = await _runtime.GraphGetAsync(new("cached"), cancellationToken);
        return JsonResource("sigmastudio://project/graph", result);
    }

    [McpServerResource(UriTemplate = "sigmastudio://catalog/adau1701", Name = "ADAU1701 block catalog", MimeType = "application/json")]
    [Description("Offline ADAU1701 block metadata and source provenance.")]
    public async Task<TextResourceContents> Catalog(CancellationToken cancellationToken)
    {
        var result = await _runtime.BlockSearchAsync("", 100, cancellationToken);
        return JsonResource("sigmastudio://catalog/adau1701", result);
    }

    [McpServerResource(UriTemplate = "sigmastudio://blocks/{catalogId}", Name = "ADAU1701 block documentation", MimeType = "application/json")]
    [Description("Normalized documentation for one ADAU1701 block.")]
    public async Task<TextResourceContents> Block(string catalogId, CancellationToken cancellationToken)
    {
        var result = await _runtime.BlockDocsAsync(catalogId, cancellationToken);
        return JsonResource($"sigmastudio://blocks/{Uri.EscapeDataString(catalogId)}", result);
    }

    [McpServerResource(UriTemplate = "sigmastudio://capture/recent", Name = "Recent Capture Window entries", MimeType = "application/json")]
    [Description("Recent Capture Window observer entries.")]
    public async Task<TextResourceContents> Capture(CancellationToken cancellationToken)
    {
        var result = await _runtime.CaptureGetAsync(new(null, 100), cancellationToken);
        return JsonResource("sigmastudio://capture/recent", result);
    }

    private static TextResourceContents JsonResource(string uri, object value) => new()
    {
        Uri = uri,
        MimeType = "application/json",
        Text = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })
    };
}
