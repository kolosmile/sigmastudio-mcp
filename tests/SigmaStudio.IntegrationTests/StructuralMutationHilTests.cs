using System.Text.Json;
using SigmaStudio.Catalog;
using SigmaStudio.Contracts;
using SigmaStudio.Core;
using SigmaStudio.Graph;

namespace SigmaStudio.IntegrationTests;

public sealed class StructuralMutationHilTests
{
    [Fact]
    [Trait("Category", "HIL")]
    public async Task Live_connection_disconnect_and_reconnect_is_reflected_in_graph()
    {
        if (!MutationEnabled()) return;
        var projectPath = RequireProject();
        var runtime = CreateRuntime();

        var before = await GetLiveGraphAsync(runtime);
        var connection = Assert.Single(before.Connections, candidate =>
            string.Equals(candidate.Source.Block, Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_SOURCE"), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Target.Block, Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_TARGET"), StringComparison.OrdinalIgnoreCase));
        var remove = await runtime.ConnectionRemoveAsync(
            new ConnectionRemoveInput(connection, new MutationInput(before.DesignRevision, NewMutationId())),
            CancellationToken.None);
        Assert.True(remove.Ok, remove.Error?.Message);

        var without = await GetLiveGraphAsync(runtime);
        Assert.DoesNotContain(without.Connections, candidate => string.Equals(candidate.Id, connection.Id, StringComparison.OrdinalIgnoreCase));

        var add = await runtime.ConnectionAddAsync(
            new ConnectionInput(
                connection.Source.Block,
                connection.Source.PinIndex,
                connection.Source.PinName,
                connection.Target.Block,
                connection.Target.PinIndex,
                connection.Target.PinName,
                new MutationInput(without.DesignRevision, NewMutationId())),
            CancellationToken.None);
        Assert.True(add.Ok, add.Error?.Message);

        var restored = await GetLiveGraphAsync(runtime);
        Assert.Contains(restored.Connections, candidate =>
            string.Equals(candidate.Source.Block, connection.Source.Block, StringComparison.OrdinalIgnoreCase) &&
            candidate.Source.PinIndex == connection.Source.PinIndex &&
            string.Equals(candidate.Target.Block, connection.Target.Block, StringComparison.OrdinalIgnoreCase) &&
            candidate.Target.PinIndex == connection.Target.PinIndex);
        Assert.Equal(projectPath, Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_PROJECT"), ignoreCase: true);
    }

    [Fact]
    [Trait("Category", "HIL")]
    public async Task Failed_structural_transaction_restores_original_fingerprint()
    {
        if (!MutationEnabled()) return;
        RequireProject();
        var runtime = CreateRuntime();
        var before = await GetLiveGraphAsync(runtime);
        Assert.NotEmpty(before.Connections);
        var connection = before.Connections.First();

        var transaction = await runtime.GraphTransactionAsync(new GraphTransactionInput(
            before.DesignRevision,
            [
                JsonSerializer.SerializeToElement(new { type = "removeConnection", connectionId = connection.Id }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "addConnection",
                    source = new { blockId = "missing-block", pinIndex = 0, pinName = "Output" },
                    target = new { blockId = "missing-block", pinIndex = 0, pinName = "Input" }
                })
            ],
            Validate: true,
            Deploy: false,
            MutationId: NewMutationId()), CancellationToken.None);

        Assert.False(transaction.Ok);
        Assert.True(transaction.RollbackAttempted);
        Assert.True(transaction.RollbackSucceeded, transaction.Error?.Message);
        var after = await GetLiveGraphAsync(runtime);
        Assert.Equal(GraphIdentity.Fingerprint(before), GraphIdentity.Fingerprint(after));
    }

    [Fact]
    [Trait("Category", "HIL")]
    public async Task Structural_change_persists_after_save_close_and_reopen()
    {
        if (!MutationEnabled()) return;
        var projectPath = RequireProject();
        var runtime = CreateRuntime();
        var before = await GetLiveGraphAsync(runtime);
        Assert.NotEmpty(before.Connections);
        var connection = before.Connections.First();

        var remove = await runtime.ConnectionRemoveAsync(
            new ConnectionRemoveInput(connection, new MutationInput(before.DesignRevision, NewMutationId())),
            CancellationToken.None);
        Assert.True(remove.Ok, remove.Error?.Message);
        var changed = await GetLiveGraphAsync(runtime);
        Assert.DoesNotContain(changed.Connections, candidate => string.Equals(candidate.Id, connection.Id, StringComparison.OrdinalIgnoreCase));

        var save = await runtime.ProjectSaveAsync(CancellationToken.None);
        Assert.True(save.Ok, save.Error?.Message);
        var close = await runtime.ProjectCloseAsync(CancellationToken.None);
        Assert.True(close.Ok, close.Error?.Message);
        var open = await runtime.ProjectOpenAsync(new ProjectOpenInput(projectPath), CancellationToken.None);
        Assert.True(open.Ok, open.Error?.Message);
        var reopened = await GetLiveGraphAsync(runtime);
        Assert.DoesNotContain(reopened.Connections, candidate =>
            string.Equals(candidate.Source.Block, connection.Source.Block, StringComparison.OrdinalIgnoreCase) &&
            candidate.Source.PinIndex == connection.Source.PinIndex &&
            string.Equals(candidate.Target.Block, connection.Target.Block, StringComparison.OrdinalIgnoreCase) &&
            candidate.Target.PinIndex == connection.Target.PinIndex);
    }

    private static SigmaRuntime CreateRuntime()
    {
        var options = new SigmaStudioOptions { AllowArbitraryPaths = true };
        var catalog = new CatalogStore();
        var automation = new NamedPipeSigmaStudioAutomation(connectTimeout: TimeSpan.FromSeconds(5));
        return new SigmaRuntime(automation, new ProjectPathPolicy(options), new GraphValidator(), () => catalog.Blocks, options);
    }

    private static async Task<ProjectGraphDto> GetLiveGraphAsync(SigmaRuntime runtime)
    {
        var result = await runtime.GraphGetAsync(new GraphGetInput("live"), CancellationToken.None);
        Assert.True(result.Ok, result.Error?.Message);
        return Assert.IsType<ProjectGraphDto>(result.Data);
    }

    private static bool MutationEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL"), "1", StringComparison.Ordinal) &&
        string.Equals(Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_MUTATION"), "1", StringComparison.Ordinal);

    private static string RequireProject()
    {
        var projectPath = Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL_PROJECT");
        Assert.False(string.IsNullOrWhiteSpace(projectPath));
        Assert.True(File.Exists(projectPath), $"SIGMASTUDIO_MCP_HIL_PROJECT does not exist: {projectPath}");
        Assert.True(projectPath!.EndsWith(".hil.dspproj", StringComparison.OrdinalIgnoreCase) ||
                    projectPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                        .IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}hil-projects{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0);
        return projectPath;
    }

    private static string NewMutationId() => Guid.NewGuid().ToString("N");
}
