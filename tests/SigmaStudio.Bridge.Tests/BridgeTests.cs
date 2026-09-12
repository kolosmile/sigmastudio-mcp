global using Xunit;
using SigmaStudio.Bridge;

namespace SigmaStudio.Bridge.Tests;

public sealed class BridgeTests
{
    [Fact]
    public void Pipe_name_is_scoped_to_current_user()
    {
        var name = BridgeOptions.PipeNameForCurrentUser();
        Assert.StartsWith("SigmaStudioMcp.", name, StringComparison.Ordinal);
        Assert.Equal("SigmaStudioMcp.".Length + 16, name.Length);
    }
}
