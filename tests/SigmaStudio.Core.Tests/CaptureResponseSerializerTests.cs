using System.Text.Json;
using SigmaStudio.Contracts;
using SigmaStudio.Core;

namespace SigmaStudio.Core.Tests;

public sealed class CaptureResponseSerializerTests
{
    [Fact]
    public void Default_projection_omits_large_raw_fields_and_keeps_fingerprint_metadata()
    {
        var rawText = new string('x', 100_000);
        var payload = JsonSerializer.SerializeToElement(new
        {
            mode = "Block Write",
            rawText,
            rawColumns = new { Data = rawText },
            value = 0.25
        });
        var entry = new CaptureEntryDto(1, DateTimeOffset.UtcNow, "write", "CAPTURE", "Block Write", Payload: payload);

        var projected = Assert.Single(CaptureResponseSerializer.Project([entry], includeRaw: false));
        var serialized = JsonSerializer.Serialize(projected);
        var projectedPayload = projected.Payload!.Value;

        Assert.DoesNotContain(rawText, serialized, StringComparison.Ordinal);
        Assert.False(projectedPayload.TryGetProperty("rawText", out _));
        Assert.False(projectedPayload.TryGetProperty("rawColumns", out _));
        Assert.Equal(rawText.Length, projectedPayload.GetProperty("raw").GetProperty("textLength").GetInt32());
        Assert.Equal(64, projectedPayload.GetProperty("raw").GetProperty("textSha256").GetString()!.Length);
    }

    [Fact]
    public void IncludeRaw_preserves_the_original_capture_fields()
    {
        var rawText = "Mode\tCell Name\nBlock Write\tGain1";
        var payload = JsonSerializer.SerializeToElement(new { rawText, rawColumns = new { column1 = "Block Write" } });
        var entry = new CaptureEntryDto(1, DateTimeOffset.UtcNow, "write", "CAPTURE", "Block Write", Payload: payload);

        var projected = Assert.Single(CaptureResponseSerializer.Project([entry], includeRaw: true));

        Assert.Equal(rawText, projected.Payload!.Value.GetProperty("rawText").GetString());
        Assert.True(projected.Payload.Value.TryGetProperty("rawColumns", out _));
    }
}
