global using Xunit;
using SigmaStudio.Catalog;
using SigmaStudio.Contracts;

namespace SigmaStudio.Catalog.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void Built_in_catalog_has_provenance_and_valid_ranges()
    {
        var blocks = new CatalogStore().Blocks;
        Assert.NotEmpty(blocks);
        Assert.Empty(new CatalogValidator().Validate(blocks));
        Assert.All(blocks, block => Assert.Equal("Analog Devices", block.Source.Publisher));
        Assert.Contains(blocks, block => block.Adau1701Compatibility == CatalogCompatibility.Supported);
    }

    [Fact]
    public void Wiki_importer_keeps_runtime_insertion_unverified()
    {
        const string page = """
            ====== Example Gain ======
            This is a documented block for ADAU1701 audio designs.
            ^ Pin ^ Description ^
            | Pin 0: Input | audio input |
            | Pin 1: Output | audio output |
            """;

        var block = new AdiWikiCatalogImporter().ParsePage(page, "https://wiki.analog.com/example.txt");

        Assert.Equal(CatalogCompatibility.Supported, block.Adau1701Compatibility);
        Assert.Equal(2, block.Inputs.Count + block.Outputs.Count);
        Assert.Equal("runtime-discovery-required", block.Automation["insert"]);
        Assert.False(block.AvailableInInstalledSigmaStudio);
        Assert.Equal("Analog Devices Wiki", block.Source.Publisher);
    }
}
