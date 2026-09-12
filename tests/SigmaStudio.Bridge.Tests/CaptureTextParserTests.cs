using SigmaStudio.Bridge;

namespace SigmaStudio.Bridge.Tests;

public sealed class CaptureTextParserTests
{
    [Fact]
    public void Parses_tsv_capture_rows_and_preserves_unknown_columns()
    {
        const string text = "Mode\tTime\tCell Name\tParameter Name\tAddress\tValue\tData\tBytes\tSender\tVendor Extra\n" +
                            "Block Write\t15:32:34 - 896ms\tGain1\tGain\t0x0012\t0.25\t00 20 00 00\t4\tIC 1\tkept";

        var row = Assert.Single(CaptureTextParser.Parse(text));

        Assert.Equal("write", row.Direction);
        Assert.Equal("Gain1", row.CellName);
        Assert.Equal("Gain", row.ParameterName);
        Assert.Equal("0x0012", row.Address);
        Assert.Equal(0.25, row.NumericValue);
        Assert.Equal(4, row.ByteCount);
        Assert.Equal("kept", row.RawColumns["Vendor Extra"]);
    }

    [Fact]
    public void Repeated_identical_rows_are_appended_as_new_events()
    {
        Assert.Equal(1, CaptureSnapshotDelta.AppendStart(["A"], ["A", "A"]));
        Assert.Equal(2, CaptureSnapshotDelta.AppendStart(["A", "A"], ["A", "A", "A"]));
        Assert.Equal(0, CaptureSnapshotDelta.AppendStart(["A", "B"], ["C"]));
        Assert.Equal(0, CaptureSnapshotDelta.AppendStart(["A", "B"], []));
    }
}
