global using Xunit;
using SigmaStudio.Core;

namespace SigmaStudio.IntegrationTests;

public sealed class IntegrationTests
{
    [Fact]
    [Trait("Category", "HIL")]
    public async Task Live_graph_extraction_is_opt_in_and_returns_stable_topology()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SIGMASTUDIO_MCP_HIL"), "1", StringComparison.Ordinal)) return;

        var automation = new NamedPipeSigmaStudioAutomation(connectTimeout: TimeSpan.FromSeconds(5));
        var refresh = await automation.ExecuteAsync(new AutomationCommand("graph.refreshLive"), CancellationToken.None);
        Assert.True(refresh.Ok, refresh.ErrorMessage);

        var first = await automation.GetSnapshotAsync(CancellationToken.None);
        var secondRefresh = await automation.ExecuteAsync(new AutomationCommand("graph.refreshLive"), CancellationToken.None);
        Assert.True(secondRefresh.Ok, secondRefresh.ErrorMessage);
        var second = await automation.GetSnapshotAsync(CancellationToken.None);

        Assert.True(first.Connected);
        Assert.Equal("SigmaStudioServer", first.Backend);
        Assert.NotEmpty(first.Graph.Blocks);
        Assert.NotEmpty(first.Graph.Connections);
        Assert.Equal(first.Graph.Blocks.Select(block => block.Id), second.Graph.Blocks.Select(block => block.Id));
        Assert.Equal(first.Graph.Connections.Select(connection => connection.Id), second.Graph.Connections.Select(connection => connection.Id));
        Assert.All(first.Graph.Connections, connection => Assert.False(string.IsNullOrWhiteSpace(connection.Id)));
    }
}
