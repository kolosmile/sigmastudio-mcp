global using Xunit;
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

        Assert.True(control.Ok);
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
}
