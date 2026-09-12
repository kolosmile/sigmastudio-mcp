global using Xunit;
using System.Text.Json;
using SigmaStudio.Catalog;
using SigmaStudio.Contracts;
using SigmaStudio.Core;
using SigmaStudio.Graph;

namespace SigmaStudio.Core.Tests;

public sealed class RuntimeTests
{
    private static SigmaRuntime CreateRuntime()
    {
        var options = new SigmaStudioOptions { AllowArbitraryPaths = true };
        var catalog = new CatalogStore();
        return new SigmaRuntime(new InMemorySigmaStudioAutomation(), new ProjectPathPolicy(options), new GraphValidator(), () => catalog.Blocks, options);
    }

    [Fact]
    public async Task Deploy_then_control_update_keeps_measurement_ready()
    {
        var runtime = CreateRuntime();
        var project = await runtime.ProjectCreateAsync(new ProjectCreateInput(Path.Combine(Path.GetTempPath(), "demo.dspproj")), CancellationToken.None);
        Assert.True(project.Ok);

        var deploy = await runtime.DeployAsync(CancellationToken.None);
        Assert.True(deploy.Ok);
        var before = await runtime.StatusAsync(CancellationToken.None);
        Assert.True(before.Data is not null);
        var control = await runtime.BlockSetControlAsync(new SetControlInput("Gain1", "Gain", 3, new MutationInput(before.DesignRevision, Guid.NewGuid().ToString("N"))), CancellationToken.None);

        Assert.True(control.Ok, control.Error?.Message);
        var ready = await runtime.ReadyForMeasurementAsync(CancellationToken.None);
        Assert.True(ready.Ok);
        Assert.Contains("readyForMeasurement", ready.Data!.ToString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stale_design_revision_is_rejected()
    {
        var runtime = CreateRuntime();
        await runtime.ProjectCreateAsync(new ProjectCreateInput(Path.Combine(Path.GetTempPath(), "demo.dspproj")), CancellationToken.None);
        var result = await runtime.BlockRenameAsync(new BlockRenameInput("Gain1", "Gain2", new MutationInput(999, Guid.NewGuid().ToString("N"))), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("STALE_REVISION", result.Error!.Code);
    }

    [Fact]
    public async Task Graph_transaction_returns_diff_and_rolls_back_failed_operation()
    {
        var runtime = CreateRuntime();
        await runtime.ProjectCreateAsync(new ProjectCreateInput(Path.Combine(Path.GetTempPath(), "transaction.dspproj")), CancellationToken.None);
        var status = await runtime.StatusAsync(CancellationToken.None);

        var success = await runtime.GraphTransactionAsync(new GraphTransactionInput(
            status.DesignRevision,
            [JsonSerializer.SerializeToElement(new { type = "addBlock", catalogId = "volume.linear_gain", objectName = "Gain2", x = 500, y = 0 })],
            Validate: true,
            MutationId: "tx-success"), CancellationToken.None);

        Assert.True(success.Ok, success.Error?.Message);
        var successData = Assert.IsType<GraphTransactionResultDto>(success.Data);
        Assert.Single(successData.Diff.BlocksAdded);

        var after = await runtime.StatusAsync(CancellationToken.None);
        var failed = await runtime.GraphTransactionAsync(new GraphTransactionInput(
            after.DesignRevision,
            [JsonSerializer.SerializeToElement(new { type = "removeBlock", blockId = "does-not-exist" })],
            Validate: true,
            MutationId: "tx-failed"), CancellationToken.None);

        Assert.False(failed.Ok);
        Assert.True(failed.RollbackAttempted);
        Assert.True(failed.RollbackSucceeded);
        Assert.Equal("BLOCK_NOT_FOUND", failed.Error!.Code);
    }
}
