global using Xunit;
using SigmaStudio.Graph;

namespace SigmaStudio.Graph.Tests;

public sealed class GraphParserTests
{
    [Fact]
    public void Parser_is_tolerant_of_unknown_nodes_and_order()
    {
        const string xml = """
            <export sampleRateHz="48000" chip="ADAU1701">
              <connections>
                <connection><target block="Output1" pinIndex="0" pinName="Input" /><source block="Gain1" pinIndex="0" pinName="Output" /></connection>
              </connections>
              <blocks>
                <block id="gain" name="Gain1" catalogId="volume.linear_gain"><outputs><pin index="0" name="Output" direction="output" /></outputs><inputs><pin index="0" name="Input" direction="input" /></inputs><mystery>ignored</mystery></block>
                <block id="output" name="Output1"><inputs><pin index="0" name="Input" direction="input" /></inputs></block>
              </blocks>
            </export>
            """;

        var parsed = new GraphParser().Parse(xml, "fixture.dspproj", 4);

        Assert.Equal(2, parsed.Graph.Blocks.Count);
        Assert.Single(parsed.Graph.Connections);
        Assert.Contains("mystery", parsed.UnknownFields);
        Assert.Equal(48000, parsed.Graph.Project.SampleRateHz);
    }
}
