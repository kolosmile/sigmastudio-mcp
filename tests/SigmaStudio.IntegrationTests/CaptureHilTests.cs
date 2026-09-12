using System.Text.Json;
using SigmaStudio.Core;

namespace SigmaStudio.IntegrationTests;

public sealed class CaptureHilTests
{
    [Fact]
    [Trait("Category", "HIL")]
    public async Task Capture_is_explicitly_unavailable_until_reenabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_CAPTURE"), "1", StringComparison.Ordinal)) return;

        var automation = new NamedPipeSigmaStudioAutomation(connectTimeout: TimeSpan.FromSeconds(5));
        var result = await automation.ExecuteAsync(new AutomationCommand("bridge.capture_get"), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("CAPTURE_UNAVAILABLE", result.ErrorCode);
    }
}
