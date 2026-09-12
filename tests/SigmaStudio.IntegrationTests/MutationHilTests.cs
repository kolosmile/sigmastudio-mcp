using System.Text.Json;
using SigmaStudio.Core;

namespace SigmaStudio.IntegrationTests;

public sealed class MutationHilTests
{
    [Fact]
    [Trait("Category", "HIL")]
    public async Task Disposable_control_write_is_read_back_captured_and_restored()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_MUTATION"), "1", StringComparison.Ordinal)) return;

        var projectPath = Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_PROJECT");
        Assert.False(string.IsNullOrWhiteSpace(projectPath));
        Assert.True(File.Exists(projectPath), $"SIGMASTUDIO_MCP_HIL_PROJECT does not exist: {projectPath}");
        Assert.True(projectPath!.EndsWith(".hil.dspproj", StringComparison.OrdinalIgnoreCase) ||
                    projectPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                        .IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}hil-projects{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0);

        var objectName = Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_OBJECT") ?? "Gain1";
        var controlName = Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_CONTROL") ?? "Gain";
        var requestedText = Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_NEW_VALUE");
        Assert.False(string.IsNullOrWhiteSpace(requestedText), "SIGMASTUDIO_MCP_HIL_NEW_VALUE is required for mutation HIL.");
        using var requestedDocument = JsonDocument.Parse(requestedText!);

        var automation = new NamedPipeSigmaStudioAutomation(connectTimeout: TimeSpan.FromSeconds(5));
        var refresh = await automation.ExecuteAsync(new AutomationCommand("graph.refreshLive"), CancellationToken.None);
        Assert.True(refresh.Ok, refresh.ErrorMessage);

        var before = await ProbeGetAsync(automation, objectName, controlName);
        var oldValue = ReturnedValue(before);
        var beforeSnapshot = await automation.GetSnapshotAsync(CancellationToken.None);
        var beforeSequence = beforeSnapshot.Capture.LastOrDefault()?.Sequence ?? 0;

        var set = await automation.ExecuteAsync(new AutomationCommand("property.probeSetControlValue", new
        {
            objectName,
            algorithmIndex = 0,
            repeatIndex = 0,
            controlName,
            value = requestedDocument.RootElement.Clone()
        }), CancellationToken.None);
        Assert.True(set.Ok, set.ErrorMessage);
        var setData = JsonSerializer.SerializeToElement(set.Data);
        Assert.True(setData.GetProperty("returnBool").GetBoolean(), setData.GetProperty("exception").GetString());
        Assert.Null(setData.GetProperty("exception").GetString());

        var after = await ProbeGetAsync(automation, objectName, controlName);
        Assert.True(JsonEquals(ReturnedValue(after), requestedDocument.RootElement), "SET read-after-write did not match the requested value.");

        var afterSnapshot = await automation.GetSnapshotAsync(CancellationToken.None);
        Assert.Contains(afterSnapshot.Capture, entry => entry.Sequence > beforeSequence);

        var restore = await automation.ExecuteAsync(new AutomationCommand("property.probeSetControlValue", new
        {
            objectName,
            algorithmIndex = 0,
            repeatIndex = 0,
            controlName,
            value = oldValue
        }), CancellationToken.None);
        Assert.True(restore.Ok, restore.ErrorMessage);
        var restored = await ProbeGetAsync(automation, objectName, controlName);
        Assert.True(JsonEquals(ReturnedValue(restored), oldValue), "Original control value was not restored.");
    }

    private static async Task<AutomationResult> ProbeGetAsync(NamedPipeSigmaStudioAutomation automation, string objectName, string controlName)
    {
        var result = await automation.ExecuteAsync(new AutomationCommand("property.probeGetControlValue", new
        {
            objectName,
            algorithmIndex = 0,
            repeatIndex = 0,
            controlName
        }), CancellationToken.None);
        Assert.True(result.Ok, result.ErrorMessage);
        return result;
    }

    private static JsonElement ReturnedValue(AutomationResult result)
    {
        var data = JsonSerializer.SerializeToElement(result.Data);
        var values = data.GetProperty("returnedValues");
        Assert.Equal(1, values.GetArrayLength());
        return values[0].Clone();
    }

    private static bool JsonEquals(JsonElement left, JsonElement right) =>
        string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
}
