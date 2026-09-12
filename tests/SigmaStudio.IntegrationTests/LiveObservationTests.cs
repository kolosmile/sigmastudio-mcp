using System.Text.Json;
using SigmaStudio.Core;

namespace SigmaStudio.IntegrationTests;

public sealed class LiveObservationTests
{
    [Fact]
    [Trait("Category", "HIL")]
    public async Task Live_get_control_value_uses_the_verified_property_contract()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL"), "1", StringComparison.Ordinal)) return;

        var objectName = Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_OBJECT") ?? "Mute1";
        var controlName = Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_CONTROL") ?? "Mute";
        var automation = new NamedPipeSigmaStudioAutomation(connectTimeout: TimeSpan.FromSeconds(5));
        var probe = await automation.ExecuteAsync(new AutomationCommand("property.probeGetControlValue", new
        {
            objectName,
            algorithmIndex = 0,
            repeatIndex = 0,
            controlName
        }), CancellationToken.None);

        Assert.True(probe.Ok, probe.ErrorMessage);
        var data = JsonSerializer.SerializeToElement(probe.Data);
        Assert.True(data.GetProperty("returnBool").GetBoolean());
        Assert.Equal(3, data.GetProperty("propertyParameters").GetArrayLength());
        Assert.Equal(1, data.GetProperty("returnedArrayLength").GetInt32());
        Assert.Null(data.GetProperty("exception").GetString());
    }

    [Fact]
    [Trait("Category", "HIL")]
    public async Task Live_status_uses_observed_sigma_ui_state()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL"), "1", StringComparison.Ordinal)) return;

        var automation = new NamedPipeSigmaStudioAutomation(connectTimeout: TimeSpan.FromSeconds(5));
        var snapshot = await automation.GetSnapshotAsync(CancellationToken.None);

        Assert.True(snapshot.Connected);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.State.ObservedUiState));
        Assert.NotNull(snapshot.State.NormalizedState);
        Assert.Equal(snapshot.State.NormalizedState, snapshot.State.SigmaStudioState);
    }
}
