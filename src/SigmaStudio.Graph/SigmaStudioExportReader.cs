using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SigmaStudio.Contracts;

namespace SigmaStudio.Graph;

/// <summary>
/// Reads the files produced by SigmaStudio's EXPORT_SYSTEM_FILES operation.
/// The NetList is authoritative for topology; the Schematic XML is used for
/// the visible module metadata and current control/parameter values.
/// </summary>
public sealed class SigmaStudioExportReader
{
    private static readonly Regex ControlPattern = new(@"(?<name>[A-Za-z][A-Za-z0-9_]*)\[(?<value>[^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex PinNumberPattern = new(@"_P(?<index>\d+)_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public GraphParseResult Read(string exportDirectory, string? projectPath = null, long designRevision = 0)
    {
        if (string.IsNullOrWhiteSpace(exportDirectory) || !Directory.Exists(exportDirectory))
            throw new DirectoryNotFoundException($"SigmaStudio export directory was not found: {exportDirectory}");

        var netListPath = Directory.EnumerateFiles(exportDirectory, "*_NetList.xml", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var schematicPath = Directory.EnumerateFiles(exportDirectory, "*.xml", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith("_NetList.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (netListPath is null) throw new FormatException("SigmaStudio export does not contain a *_NetList.xml file.");
        if (schematicPath is null) throw new FormatException("SigmaStudio export does not contain a schematic XML file.");

        var schematic = ParseSchematic(XDocument.Load(schematicPath, LoadOptions.PreserveWhitespace));
        var netList = ParseNetList(XDocument.Load(netListPath, LoadOptions.PreserveWhitespace));
        return BuildGraph(projectPath, designRevision, schematic, netList, Path.GetFileNameWithoutExtension(schematicPath));
    }

    private static SchematicInfo ParseSchematic(XDocument document)
    {
        var root = document.Root ?? throw new FormatException("SigmaStudio schematic XML has no root element.");
        var chip = root.Descendants().FirstOrDefault(e => NameIs(e, "PartNumber"))?.Value.Trim();
        var modules = new Dictionary<string, ModuleInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in root.Descendants().Where(e => NameIs(e, "Module")))
        {
            var cell = ChildValue(module, "CellName");
            if (string.IsNullOrWhiteSpace(cell)) continue;
            var algorithms = new List<AlgorithmInfo>();
            foreach (var algorithm in module.Elements().Where(e => NameIs(e, "Algorithm")))
            {
                var detailedName = ChildValue(algorithm, "DetailedName") ?? ChildValue(algorithm, "AlgoName") ?? "";
                var description = ChildValue(algorithm, "Description") ?? "";
                var controls = ParseControls(description);
                var parameters = algorithm.Descendants().Where(e => NameIs(e, "ModuleParameter"))
                    .Select(ParseParameter)
                    .Where(p => p is not null)
                    .Cast<ParameterDto>()
                    .ToArray();
                algorithms.Add(new AlgorithmInfo(detailedName, controls, parameters));
            }
            modules[cell!] = new ModuleInfo(cell!, algorithms);
        }

        return new SchematicInfo(chip, modules);
    }

    private static IReadOnlyList<NetlistAlgorithm> ParseNetList(XDocument document)
    {
        var root = document.Root ?? throw new FormatException("SigmaStudio NetList XML has no root element.");
        var processor = root.Descendants().FirstOrDefault(e => NameIs(e, "IC"))?.Attribute("name")?.Value.Trim() ?? "IC 1";
        return root.Descendants().Where(e => NameIs(e, "Algorithm")).Select((element, index) =>
        {
            var name = AttributeValue(element, "name") ?? $"Algorithm{index + 1}";
            var friendlyName = AttributeValue(element, "friendlyname") ?? name;
            var cell = AttributeValue(element, "cell") ?? name;
            var location = ParseLocation(AttributeValue(element, "location"));
            var sampleRateHz = ParseInt(AttributeValue(element, "FS"), 48000);
            var links = element.Elements().Where(e => NameIs(e, "Link")).Select(link => new NetlistLink(
                AttributeValue(link, "link") ?? "",
                AttributeValue(link, "pin") ?? "",
                string.Equals(AttributeValue(link, "dir"), "out", StringComparison.OrdinalIgnoreCase))).Where(l => l.LinkId.Length > 0).ToArray();
            return new NetlistAlgorithm(processor, name, friendlyName, cell, location, sampleRateHz, links);
        }).ToArray();
    }

    private static GraphParseResult BuildGraph(string? projectPath, long designRevision, SchematicInfo schematic, IReadOnlyList<NetlistAlgorithm> netlist, string exportName)
    {
        if (netlist.Count == 0) throw new FormatException("SigmaStudio NetList contains no Algorithm elements.");

        var blocks = new List<BlockDto>();
        var byKey = new Dictionary<string, BlockBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var algorithm in netlist)
        {
            var key = $"{algorithm.Processor}\u001f{algorithm.Cell}";
            if (!byKey.TryGetValue(key, out var builder))
            {
                builder = new BlockBuilder(algorithm.Processor, algorithm.Cell, algorithm.Location);
                byKey.Add(key, builder);
            }
            builder.Algorithms.Add(algorithm);
        }

        var objectNames = MakeObjectNames(byKey.Values);
        foreach (var builder in byKey.Values)
        {
            var id = StableId("block", builder.Processor, builder.Cell);
            var module = schematic.Modules.TryGetValue(builder.Cell, out var found) ? found : null;
            var controls = module?.Algorithms.SelectMany(a => a.Controls).GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray() ?? [];
            var parameters = module?.Algorithms.SelectMany(a => a.Parameters).GroupBy(p => $"{p.Name}|{p.Address}", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray() ?? [];
            var inputs = builder.Algorithms.SelectMany(a => a.Links.Where(l => !l.IsOutput).Select(l => l.PinName)).Distinct(StringComparer.OrdinalIgnoreCase).Select((name, index) => new PinDto(PinIndex(name, index), name, "input")).OrderBy(p => p.Index).ToArray();
            var outputs = builder.Algorithms.SelectMany(a => a.Links.Where(l => l.IsOutput).Select(l => l.PinName)).Distinct(StringComparer.OrdinalIgnoreCase).Select((name, index) => new PinDto(PinIndex(name, index), name, "output")).OrderBy(p => p.Index).ToArray();
            var first = builder.Algorithms[0];
            var catalogId = CatalogId(first.FriendlyName, first.Name);
            blocks.Add(new BlockDto(
                id,
                objectNames[$"{builder.Processor}\u001f{builder.Cell}"],
                builder.Cell,
                catalogId,
                first.FriendlyName,
                builder.Algorithms.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                inputs,
                outputs,
                controls,
                parameters,
                first.Location,
                builder.Cell,
                first.FriendlyName,
                string.Join("/", builder.Algorithms.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase))));
        }

        var blockByKey = byKey.ToDictionary(pair => pair.Key, pair => blocks.First(b => b.Id == StableId("block", pair.Value.Processor, pair.Value.Cell)), StringComparer.OrdinalIgnoreCase);
        var linkEndpoints = new Dictionary<string, List<(NetlistAlgorithm Algorithm, NetlistLink Link)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var algorithm in netlist)
        foreach (var link in algorithm.Links)
        {
            if (!linkEndpoints.TryGetValue(link.LinkId, out var endpoints)) linkEndpoints[link.LinkId] = endpoints = [];
            endpoints.Add((algorithm, link));
        }

        var connections = new List<ConnectionDto>();
        var warnings = new List<string>();
        foreach (var pair in linkEndpoints.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var producers = pair.Value.Where(e => e.Link.IsOutput).ToArray();
            var consumers = pair.Value.Where(e => !e.Link.IsOutput).ToArray();
            if (producers.Length == 0 || consumers.Length == 0)
            {
                warnings.Add($"NetList link '{pair.Key}' has {(producers.Length == 0 ? "no producer" : "no consumer")}.");
                continue;
            }
            foreach (var producer in producers)
            foreach (var consumer in consumers)
            {
                var sourceBlock = blockByKey[$"{producer.Algorithm.Processor}\u001f{producer.Algorithm.Cell}"];
                var targetBlock = blockByKey[$"{consumer.Algorithm.Processor}\u001f{consumer.Algorithm.Cell}"];
                if (sourceBlock.Id == targetBlock.Id) continue;
                var source = new PinRefDto(sourceBlock.ObjectName, PinIndex(producer.Link.PinName, 0), producer.Link.PinName, sourceBlock.Id);
                var target = new PinRefDto(targetBlock.ObjectName, PinIndex(consumer.Link.PinName, 0), consumer.Link.PinName, targetBlock.Id);
                var id = StableId("connection", sourceBlock.Id, producer.Link.PinName, targetBlock.Id, consumer.Link.PinName, pair.Key);
                connections.Add(new ConnectionDto(source, target, id, pair.Key));
            }
        }

        var sampleRate = netlist.Select(a => a.SampleRateHz).FirstOrDefault(rate => rate > 0);
        if (sampleRate <= 0) sampleRate = 48000;
        var project = new ProjectIdentityDto(projectPath, string.IsNullOrWhiteSpace(schematic.Chip) ? "ADAU1701" : schematic.Chip!, sampleRate);
        var unknown = warnings;
        unknown.Add($"Source export: {exportName}; topology parsed from *_NetList.xml.");
        var graph = new ProjectGraphDto(project, blocks.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray(), connections.GroupBy(c => c.Id ?? string.Empty, StringComparer.Ordinal).Select(g => g.First()).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray(), designRevision, GraphFreshness.Fresh, unknown);
        return new GraphParseResult { Graph = GraphIdentity.WithIdentity(graph), UnknownFields = unknown.ToArray() };
    }

    private static Dictionary<string, string> MakeObjectNames(IEnumerable<BlockBuilder> builders)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in builders.GroupBy(b => b.Cell, StringComparer.OrdinalIgnoreCase))
        {
            var duplicate = group.Count() > 1;
            foreach (var builder in group) result[$"{builder.Processor}\u001f{builder.Cell}"] = duplicate ? $"{builder.Cell}::{builder.Processor}" : builder.Cell;
        }
        return result;
    }

    private static int PinIndex(string pinName, int fallback) => int.TryParse(PinNumberPattern.Match(pinName).Groups["index"].Value, out var index) ? index : fallback;

    private static string CatalogId(string friendlyName, string nativeName)
    {
        if (friendlyName.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0) return "io.audio_input";
        if (friendlyName.IndexOf("output", StringComparison.OrdinalIgnoreCase) >= 0) return "io.audio_output";
        if (friendlyName.IndexOf("gain", StringComparison.OrdinalIgnoreCase) >= 0 && friendlyName.IndexOf("mixer", StringComparison.OrdinalIgnoreCase) < 0) return "volume.linear_gain";
        if (friendlyName.IndexOf("parametric", StringComparison.OrdinalIgnoreCase) >= 0 || friendlyName.IndexOf("peq", StringComparison.OrdinalIgnoreCase) >= 0) return "filter.parametric_eq";
        var normalized = Regex.Replace(friendlyName, @"[^A-Za-z0-9]+", "_").Trim('_').ToLowerInvariant();
        return normalized.Length == 0 ? $"adi.native.{nativeName}" : $"adi.native.{normalized}";
    }

    private static string StableId(string prefix, params string[] parts)
    {
        var canonical = string.Join("\u001f", parts.Select(p => p.Trim()));
        byte[] bytes;
        using (var sha = SHA256.Create()) bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        var hex = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        return $"{prefix}-{hex.Substring(0, 16)}";
    }

    private static IReadOnlyList<ControlDto> ParseControls(string description)
    {
        return ControlPattern.Matches(description).Cast<Match>().Select(match =>
        {
            var name = match.Groups["name"].Value;
            var valueText = match.Groups["value"].Value.Trim();
            var value = ParseNumber(valueText);
            var isBoolean = bool.TryParse(valueText, out _);
            var enumValues = isBoolean ? new[] { "false", "true" } : null;
            var controlId = "ctrl_" + Regex.Replace(name, @"[^A-Za-z0-9]+", "_").Trim('_').ToLowerInvariant();
            return new ControlDto(name, value, null, null, enumValues, null, controlId, null, isBoolean ? "boolean" : "number", "unknown", "live-sigmastudio", GraphFreshness.Fresh);
        }).GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
    }

    private static ParameterDto? ParseParameter(XElement element)
    {
        var name = AttributeValue(element, "Name") ?? ChildValue(element, "Name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var address = AttributeValue(element, "Address") ?? ChildValue(element, "Address");
        var format = AttributeValue(element, "Type") ?? ChildValue(element, "Type");
        var value = ParseNumber(AttributeValue(element, "Value") ?? ChildValue(element, "Value"));
        return new ParameterDto(name!, address, format, value);
    }

    private static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (bool.TryParse(text, out var boolean)) return boolean ? 1 : 0;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return null;
    }

    private static GraphPositionDto? ParseLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var matches = Regex.Matches(value, @"[XY]\s*=\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (matches.Count < 2) return null;
        return new GraphPositionDto(ParseDouble(matches[0].Groups[1].Value), ParseDouble(matches[1].Groups[1].Value));
    }

    private static double ParseDouble(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static int ParseInt(string? value, int fallback) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
    private static string? AttributeValue(XElement element, string name) => element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
    private static string? ChildValue(XElement element, string name) => element.Elements().FirstOrDefault(e => NameIs(e, name))?.Value.Trim();
    private static bool NameIs(XElement element, string name) => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    private sealed record SchematicInfo(string? Chip, IReadOnlyDictionary<string, ModuleInfo> Modules);
    private sealed record ModuleInfo(string Cell, IReadOnlyList<AlgorithmInfo> Algorithms);
    private sealed record AlgorithmInfo(string Name, IReadOnlyList<ControlDto> Controls, IReadOnlyList<ParameterDto> Parameters);
    private sealed record NetlistAlgorithm(string Processor, string Name, string FriendlyName, string Cell, GraphPositionDto? Location, int SampleRateHz, IReadOnlyList<NetlistLink> Links);
    private sealed record NetlistLink(string LinkId, string PinName, bool IsOutput);
    private sealed class BlockBuilder(string processor, string cell, GraphPositionDto? location)
    {
        public string Processor { get; } = processor;
        public string Cell { get; } = cell;
        public GraphPositionDto? Location { get; } = location;
        public List<NetlistAlgorithm> Algorithms { get; } = [];
    }
}
