using System.Globalization;
using System.Text;

namespace SigmaStudio.Bridge;

public sealed record CaptureParsedRow(
    string RawText,
    IReadOnlyDictionary<string, string> RawColumns,
    string? Mode,
    string? Time,
    string? CellName,
    string? ParameterName,
    string? Address,
    string? ValueText,
    double? NumericValue,
    string? Data,
    int? ByteCount,
    string? Sender)
{
    public string Direction => Mode != null && Mode.IndexOf("write", StringComparison.OrdinalIgnoreCase) >= 0 ? "write" :
        Mode != null && Mode.IndexOf("read", StringComparison.OrdinalIgnoreCase) >= 0 ? "read" : "observation";

    public string Summary => string.Join(" | ", new[] { Mode, Time, CellName, ParameterName, Address, ValueText, Data, ByteCount?.ToString(CultureInfo.InvariantCulture), Sender }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public static class CaptureTextParser
{
    public static IReadOnlyList<CaptureParsedRow> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split(new[] { '\n' }, StringSplitOptions.None)
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0) return [];

        var headerIndex = Array.FindIndex(lines, IsHeaderLine);
        var headers = headerIndex >= 0 ? SplitColumns(lines[headerIndex]) : [];
        var start = headerIndex >= 0 ? headerIndex + 1 : 0;
        var rows = new List<CaptureParsedRow>();
        for (var index = start; index < lines.Length; index++)
        {
            var line = lines[index];
            if (IsSeparator(line)) continue;
            var columns = SplitColumns(line);
            rows.Add(ParseRow(line, headers, columns));
        }
        return rows;
    }

    private static CaptureParsedRow ParseRow(string rawText, IReadOnlyList<string> headers, IReadOnlyList<string> values)
    {
        var rawColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < values.Count; index++)
        {
            var key = index < headers.Count && !string.IsNullOrWhiteSpace(headers[index]) ? headers[index] : $"column{index + 1}";
            if (rawColumns.ContainsKey(key)) key = $"{key}#{index + 1}";
            rawColumns[key] = values[index];
        }

        string? Column(params string[] names) => rawColumns.FirstOrDefault(pair => names.Any(name => string.Equals(Normalize(pair.Key), Normalize(name), StringComparison.OrdinalIgnoreCase))).Value;
        var valueText = Column("Value");
        return new CaptureParsedRow(
            rawText,
            rawColumns,
            Column("Mode"),
            Column("Time"),
            Column("Cell Name", "Name"),
            Column("Parameter Name"),
            Column("Address"),
            valueText,
            TryParseDouble(valueText),
            Column("Data"),
            TryParseInt(Column("Bytes")),
            Column("Sender"));
    }

    private static IReadOnlyList<string> SplitColumns(string line)
    {
        if (line.Contains('\t')) return line.Split('\t').Select(value => value.Trim()).ToArray();
        var columns = System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{2,}")
            .Where(value => value.Length > 0)
            .Select(value => value.Trim())
            .ToArray();
        return columns.Length > 1 ? columns : [line.Trim()];
    }

    private static bool IsHeaderLine(string line)
    {
        var normalized = Normalize(line);
        return normalized.IndexOf("mode", StringComparison.OrdinalIgnoreCase) >= 0 &&
               (normalized.IndexOf("parametername", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("cellname", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsSeparator(string line) => line.All(character => character is '-' or '=' or '\t' or ' ');

    private static string Normalize(string value) => new(value.Where(character => !char.IsWhiteSpace(character) && character != '_' && character != '-').ToArray());

    private static double? TryParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed :
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed) ? parsed : null;

    private static int? TryParseInt(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : null;
    }
}

public static class CaptureSnapshotDelta
{
    public static int AppendStart(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var prefix = 0;
        while (prefix < previous.Count && prefix < current.Count && string.Equals(previous[prefix], current[prefix], StringComparison.Ordinal)) prefix++;
        return previous.Count == 0 || prefix == previous.Count ? prefix : 0;
    }
}
