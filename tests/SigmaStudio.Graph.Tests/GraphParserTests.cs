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

    [Fact]
    public void SigmaStudio_export_reader_normalizes_real_netlist_shape_with_stable_ids()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sigmastudio-mcp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixture.xml"), """
                <Schematic>
                  <IC><PartNumber>ADAU1701</PartNumber><Module>
                    <CellName>Mute1</CellName>
                    <Algorithm><DetailedName>MuteNoSlewAlg1</DetailedName><Description>Mute[False]</Description>
                      <ModuleParameter Name="MuteNoSlewAlg1mute" Type="FixedPoint" Address="3" Value="1" />
                    </Algorithm>
                  </Module></IC>
                </Schematic>
                """);
            File.WriteAllText(Path.Combine(directory, "fixture_NetList.xml"), """
                <NetList><IC name=" IC 1 " type="DSPSigma100"><Schematic>
                  <Algorithm name="ICSigma100In1" friendlyname="170x input" cell="Input1" location="{X=0, Y=0}" FS="44100">
                    <Link pin="O_C0_A0_P1_out" dir="out" link="Link1" />
                  </Algorithm>
                  <Algorithm name="MuteNoSlewAlg1" friendlyname="No Slew" cell="Mute1" location="{X=100, Y=0}" FS="44100">
                    <Link pin="I_C1_A0_P1_in" dir="in" link="Link1" />
                    <Link pin="O_C1_A0_P2_out" dir="out" link="Link2" />
                  </Algorithm>
                  <Algorithm name="ICSigma100Out1" friendlyname="170x output" cell="Output1" location="{X=200, Y=0}" FS="44100">
                    <Link pin="I_C2_A0_P1_in" dir="in" link="Link2" />
                  </Algorithm>
                </Schematic></IC></NetList>
                """);

            var reader = new SigmaStudioExportReader();
            var first = reader.Read(directory, "fixture.dspproj", 7).Graph;
            var second = reader.Read(directory, "fixture.dspproj", 7).Graph;

            Assert.Equal(44100, first.Project.SampleRateHz);
            Assert.Equal(3, first.Blocks.Count);
            Assert.Equal(2, first.Connections.Count);
            Assert.Equal(first.Blocks.Select(b => b.Id), second.Blocks.Select(b => b.Id));
            Assert.Equal(first.Connections.Select(c => c.Id), second.Connections.Select(c => c.Id));
            Assert.Equal(0, first.Blocks.Single(b => b.ObjectName == "Mute1").Controls.Single(c => c.Name == "Mute").Value);
            Assert.All(first.Connections, connection => Assert.NotNull(connection.Id));
            Assert.All(first.Connections, connection => Assert.NotNull(connection.Source.BlockId));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
