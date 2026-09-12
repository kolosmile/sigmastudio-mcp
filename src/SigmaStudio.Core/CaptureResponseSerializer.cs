using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SigmaStudio.Contracts;

namespace SigmaStudio.Core;

/// <summary>
/// Projects Capture entries into the MCP response shape without echoing large
/// clipboard lines by default.
/// </summary>
public static class CaptureResponseSerializer
{
    public static IReadOnlyList<CaptureEntryDto> Project(IReadOnlyList<CaptureEntryDto> entries, bool includeRaw)
    {
        if (includeRaw) return entries;
        return entries.Select(ProjectEntry).ToArray();
    }

    public static object Metadata(bool includeRaw) => new
    {
        format = "structured-json",
        rawFields = includeRaw ? "included" : "omitted",
        rawText = includeRaw ? "plain-text" : "sha256-metadata"
    };

    private static CaptureEntryDto ProjectEntry(CaptureEntryDto entry)
    {
        if (entry.Payload is not JsonElement payload || payload.ValueKind != JsonValueKind.Object)
            return entry;

        var compact = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        string? rawText = null;
        var rawColumnsPresent = false;
        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, "rawText", StringComparison.OrdinalIgnoreCase))
            {
                rawText = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
                continue;
            }

            if (string.Equals(property.Name, "rawColumns", StringComparison.OrdinalIgnoreCase))
            {
                rawColumnsPresent = true;
                continue;
            }

            compact[property.Name] = property.Value.Clone();
        }

        if (rawText is null && !rawColumnsPresent)
            return entry;

        var rawMetadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["available"] = JsonSerializer.SerializeToElement(true)
        };
        if (rawText is not null)
        {
            rawMetadata["textLength"] = JsonSerializer.SerializeToElement(rawText.Length);
            rawMetadata["textSha256"] = JsonSerializer.SerializeToElement(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawText))).ToLowerInvariant());
        }
        if (rawColumnsPresent)
            rawMetadata["columnsAvailable"] = JsonSerializer.SerializeToElement(true);

        compact["raw"] = JsonSerializer.SerializeToElement(rawMetadata).Clone();
        return entry with { Payload = JsonSerializer.SerializeToElement(compact).Clone() };
    }
}
