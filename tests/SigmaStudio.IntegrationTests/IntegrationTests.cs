global using Xunit;

namespace SigmaStudio.IntegrationTests;

public sealed class IntegrationTests
{
    [Fact]
    [Trait("Category", "HIL")]
    public void Real_SigmaStudio_tests_are_opt_in() => Assert.True(true);
}
