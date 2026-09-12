using SigmaStudio.Bridge;
using SigmaStudio.Contracts;

namespace SigmaStudio.Bridge.Tests;

public sealed class UiStateNormalizerTests
{
    [Theory]
    [InlineData("Active: Downloaded", SigmaStudioState.ActiveDownloaded)]
    [InlineData("100% Active: Downloaded", SigmaStudioState.ActiveDownloaded)]
    [InlineData("Ready: Compiled", SigmaStudioState.ReadyCompiled)]
    [InlineData("Ready - Download", SigmaStudioState.ReadyCompiled)]
    [InlineData("Design Mode", SigmaStudioState.DesignMode)]
    [InlineData("unexpected state", SigmaStudioState.Unknown)]
    public void Normalizes_observed_status_without_losing_unknowns(string rawText, SigmaStudioState expected)
    {
        Assert.Equal(expected, UiStateNormalizer.Normalize(rawText));
    }

    [Fact]
    public void Empty_status_is_not_claimed_as_a_state()
    {
        Assert.Null(UiStateNormalizer.Normalize(null));
        Assert.Null(UiStateNormalizer.Normalize("  "));
    }
}
