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
}
