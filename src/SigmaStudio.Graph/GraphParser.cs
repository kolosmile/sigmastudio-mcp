using System.Xml.Linq;
using SigmaStudio.Contracts;

namespace SigmaStudio.Graph;

public sealed class GraphParseResult
{
    public required ProjectGraphDto Graph { get; init; }
    public required IReadOnlyList<string> UnknownFields { get; init; }
}

public sealed class GraphParser
{
    public GraphParseResult Parse(string xml, string? projectPath, long designRevision)
    {
        var unknown = new List<string>();
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new FormatException("Export XML has no root element.");

        var project = new ProjectIdentityDto(
            projectPath,
            (string?)root.Attribute("chip") ?? FindValue(root, "Chip") ?? "ADAU1701",
            ParseInt((string?)root.Attribute("sampleRateHz") ?? FindValue(root, "SampleRateHz"), 48000));

        var blocks = root.Descendants().Where(IsBlockElement).Select((element, index) => ParseBlock(element, index, unknown)).ToList();
        var connections = root.Descendants().Where(IsConnectionElement).Select(ParseConnection).Where(c => c is not null).Cast<ConnectionDto>().ToList();

        var knownRootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "project", "schematic", "blocks", "block", "objects", "connections", "connection", "nodes", "node"
        };
        foreach (var element in root.Descendants())
        {
            if (!knownRootNames.Contains(element.Name.LocalName) && element.Elements().Any())
            {
                unknown.Add(element.Name.LocalName);
            }
        }

        return new GraphParseResult
        {
            Graph = new ProjectGraphDto(project, blocks, connections, designRevision, GraphFreshness.Fresh, unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
            UnknownFields = unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static BlockDto ParseBlock(XElement element, int index, ICollection<string> unknown)
    {
        var objectName = (string?)element.Attribute("objectName") ?? (string?)element.Attribute("name") ?? FindValue(element, "ObjectName") ?? $"Block{index + 1}";
        var catalogId = (string?)element.Attribute("catalogId") ?? FindValue(element, "CatalogId") ?? "unknown";
        var displayName = (string?)element.Attribute("displayName") ?? FindValue(element, "DisplayName") ?? objectName;
        var algorithms = ChildValues(element, "algorithm", "Algorithm");
        var inputs = ParsePins(element, "inputs", "input", "Inputs", "Input");
        var outputs = ParsePins(element, "outputs", "output", "Outputs", "Output");
        var controls = element.Descendants().Where(e => NameIs(e, "control", "Control"))
            .Select(e => new ControlDto(
                (string?)e.Attribute("name") ?? FindValue(e, "Name") ?? "Unknown",
                ParseNullableDouble((string?)e.Attribute("value") ?? FindValue(e, "Value")),
                ParseNullableDouble((string?)e.Attribute("min") ?? FindValue(e, "Min")),
                ParseNullableDouble((string?)e.Attribute("max") ?? FindValue(e, "Max")),
                ChildValues(e, "enum", "Enum"),
                (string?)e.Attribute("unit") ?? FindValue(e, "Unit")))
            .ToList();
        var parameters = element.Descendants().Where(e => NameIs(e, "parameter", "Parameter"))
            .Select(e => new ParameterDto(
                (string?)e.Attribute("name") ?? FindValue(e, "Name") ?? "Unknown",
                (string?)e.Attribute("address") ?? FindValue(e, "Address"),
                (string?)e.Attribute("format") ?? FindValue(e, "Format"),
                ParseNullableDouble((string?)e.Attribute("value") ?? FindValue(e, "Value"))))
            .ToList();

        var known = new HashSet<string>(new[] { "block", "Block", "inputs", "outputs", "algorithms", "controls", "parameters", "position" }, StringComparer.OrdinalIgnoreCase);
        foreach (var child in element.Elements())
        {
            if (!known.Contains(child.Name.LocalName)) unknown.Add(child.Name.LocalName);
        }

        return new BlockDto(
            (string?)element.Attribute("id") ?? $"block-{index + 1}",
            objectName,
            (string?)element.Attribute("fullObjectName") ?? objectName,
            catalogId,
            displayName,
            algorithms,
            inputs,
            outputs,
            controls,
            parameters,
            ParsePosition(element));
    }

    private static ConnectionDto? ParseConnection(XElement element)
    {
        var source = ParseEndpoint(element, "source", "Source");
        var target = ParseEndpoint(element, "target", "Target");
        return source is null || target is null ? null : new ConnectionDto(source, target);
    }

    private static PinRefDto? ParseEndpoint(XElement element, string lowerName, string upperName)
    {
        var endpoint = element.Elements().FirstOrDefault(e => NameIs(e, lowerName, upperName));
        if (endpoint is null) return null;
        var block = (string?)endpoint.Attribute("block") ?? FindValue(endpoint, "Block");
        var name = (string?)endpoint.Attribute("pinName") ?? (string?)endpoint.Attribute("name") ?? FindValue(endpoint, "PinName") ?? "";
        var index = ParseInt((string?)endpoint.Attribute("pinIndex") ?? (string?)endpoint.Attribute("index") ?? FindValue(endpoint, "PinIndex"), 0);
        return string.IsNullOrWhiteSpace(block) ? null : new PinRefDto(block, index, name);
    }

    private static IReadOnlyList<PinDto> ParsePins(XElement element, params string[] names)
    {
        return element.Descendants().Where(e => names.Any(n => NameIs(e, n))).Select((e, index) =>
            new PinDto(ParseInt((string?)e.Attribute("index") ?? FindValue(e, "Index"), index),
                (string?)e.Attribute("name") ?? FindValue(e, "Name") ?? $"Pin{index}",
                (string?)e.Attribute("direction") ?? FindValue(e, "Direction") ?? "unknown")).ToList();
    }

    private static GraphPositionDto? ParsePosition(XElement element)
    {
        var position = element.Elements().FirstOrDefault(e => NameIs(e, "position", "Position"));
        if (position is null) return null;
        return new GraphPositionDto(
            ParseDouble((string?)position.Attribute("x") ?? FindValue(position, "X"), 0),
            ParseDouble((string?)position.Attribute("y") ?? FindValue(position, "Y"), 0));
    }

    private static bool IsBlockElement(XElement element) => NameIs(element, "block", "Block", "object", "Object", "node", "Node") &&
        (element.Attribute("objectName") is not null || element.Attribute("name") is not null || element.Elements().Any(e => NameIs(e, "controls", "Control", "inputs", "Input")));

    private static bool IsConnectionElement(XElement element) => NameIs(element, "connection", "Connection", "wire", "Wire");

    private static bool NameIs(XElement element, params string[] names) => names.Any(n => string.Equals(element.Name.LocalName, n, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ChildValues(XElement element, params string[] names) => element.Descendants().Where(e => names.Any(n => NameIs(e, n))).Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToArray();

    private static string? FindValue(XElement element, string name) => element.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var result) ? result : fallback;
    private static double ParseDouble(string? value, double fallback) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : fallback;
    private static double? ParseNullableDouble(string? value) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;
}
