using System.Text.Json;
using SigmaStudio.Core;

namespace SigmaStudio.IntegrationTests;

public sealed class CaptureHilTests
{
    [Fact]
    [Trait("Category", "HIL")]
    public async Task Selected_capture_row_copy_is_structured_and_non_empty()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_CAPTURE"), "1", StringComparison.Ordinal)) return;

        var automation = new NamedPipeSigmaStudioAutomation(connectTimeout: TimeSpan.FromSeconds(5));
        var result = await automation.ExecuteAsync(new AutomationCommand("bridge.capture_get"), CancellationToken.None);
        Assert.True(result.Ok, $"{result.ErrorCode}: {result.ErrorMessage}");
        var data = JsonSerializer.SerializeToElement(result.Data);
        var entries = data.GetProperty("entries");

        Assert.True(entries.GetArrayLength() > 1, data.GetProperty("warning").GetString() ?? "Capture returned fewer than two selected-range entries.");
        Assert.Null(data.GetProperty("warning").GetString());
        Assert.Contains("selected-range", data.GetProperty("copyAction").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains(entries.EnumerateArray(), entry => entry.GetProperty("category").GetString() == "CAPTURE" && entry.TryGetProperty("payload", out var payload) && payload.ValueKind != JsonValueKind.Null);
    }
}
