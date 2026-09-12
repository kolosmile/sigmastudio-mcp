using System.Text.Json;
using SigmaStudio.Catalog;
using SigmaStudio.Contracts;
using SigmaStudio.Core;
using SigmaStudio.Graph;

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
        var runtime = CreateRuntime(automation);

        var graph = await runtime.GraphGetAsync(new GraphGetInput("live"), CancellationToken.None);
        Assert.True(graph.Ok, graph.Error?.Message);
        var snapshot = await automation.GetSnapshotAsync(CancellationToken.None);
        var block = snapshot.Graph.Blocks.FirstOrDefault(candidate =>
            string.Equals(candidate.ObjectName, objectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id, objectName, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(block);
        var control = block!.Controls.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, controlName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.ControlId, controlName, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(control);
        var oldValue = CurrentValue(control!);
        Assert.NotEqual(JsonValueKind.Undefined, oldValue.ValueKind);

        var set = await runtime.BlockSetControlAsync(
            new SetControlInput(objectName, controlName, requestedDocument.RootElement.Clone(),
                new MutationInput(snapshot.State.DesignRevision, Guid.NewGuid().ToString("N"))),
            CancellationToken.None);
        Assert.True(set.Ok, set.Error?.Message);
        var setData = JsonSerializer.SerializeToElement(set.Data);
        Assert.True(setData.GetProperty("verified").GetBoolean());
        Assert.True(setData.GetProperty("captureEvidence").GetProperty("available").GetBoolean());

        var after = await runtime.BlockGetAsync(objectName, refreshControls: true, CancellationToken.None);
        Assert.True(after.Ok, after.Error?.Message);
        var observed = FindControl(JsonSerializer.SerializeToElement(after.Data), controlName);
        Assert.True(JsonEquals(observed, requestedDocument.RootElement), "Production SET read-after-write did not match the requested value.");

        var restoreSnapshot = await automation.GetSnapshotAsync(CancellationToken.None);
        var restore = await runtime.BlockSetControlAsync(
            new SetControlInput(objectName, controlName, oldValue,
                new MutationInput(restoreSnapshot.State.DesignRevision, Guid.NewGuid().ToString("N"))),
            CancellationToken.None);
        Assert.True(restore.Ok, restore.Error?.Message);

        var restored = await runtime.BlockGetAsync(objectName, refreshControls: true, CancellationToken.None);
        Assert.True(restored.Ok, restored.Error?.Message);
        var restoredValue = FindControl(JsonSerializer.SerializeToElement(restored.Data), controlName);
        Assert.True(JsonEquals(restoredValue, oldValue), "Original control value was not restored.");
    }

    private static SigmaRuntime CreateRuntime(NamedPipeSigmaStudioAutomation automation)
    {
        var options = new SigmaStudioOptions { AllowArbitraryPaths = true };
        return new SigmaRuntime(automation, new ProjectPathPolicy(options), new GraphValidator(), () => new CatalogStore().Blocks, options);
    }

    private static JsonElement CurrentValue(ControlDto control) =>
        control.TypedValue is JsonElement typed && typed.ValueKind != JsonValueKind.Undefined
            ? typed.Clone()
            : control.Value is double numeric
                ? ControlValueJson.Number(numeric)
                : default;

    private static JsonElement FindControl(JsonElement blockData, string controlName)
    {
        var controls = blockData.ValueKind == JsonValueKind.Object && blockData.TryGetProperty("controls", out var blockControls)
            ? blockControls
            : blockData;
        foreach (var control in controls.EnumerateArray())
        {
            if (string.Equals(control.GetProperty("name").GetString(), controlName, StringComparison.OrdinalIgnoreCase) ||
                control.TryGetProperty("controlId", out var id) && string.Equals(id.GetString(), controlName, StringComparison.OrdinalIgnoreCase))
            {
                if (control.TryGetProperty("typedValue", out var typed) && typed.ValueKind != JsonValueKind.Null && typed.ValueKind != JsonValueKind.Undefined) return typed.Clone();
                if (control.TryGetProperty("value", out var value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined) return value.Clone();
            }
        }
        return default;
    }

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
            return left.GetDouble() == right.GetDouble();
        return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
    }
}
