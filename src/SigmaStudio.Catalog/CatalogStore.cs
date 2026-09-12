using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SigmaStudio.Contracts;

namespace SigmaStudio.Catalog;

public sealed class CatalogStore
{
    private readonly IReadOnlyList<CatalogBlockDto> _blocks;

    public CatalogStore(string? catalogPath = null)
    {
        _blocks = TryLoad(catalogPath) ?? BuiltIn();
    }

    public string Version => "1.0.0";
    public IReadOnlyList<CatalogBlockDto> Blocks => _blocks;

    private static IReadOnlyList<CatalogBlockDto>? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<CatalogBlockDto>>(json, JsonOptions);
    }

    public static IReadOnlyList<CatalogBlockDto> BuiltIn()
    {
        var source = new CatalogSourceDto("Analog Devices", "2026-09-12T00:00:00Z", Hash("SigmaStudio MCP built-in catalog 1"), "https://wiki.analog.com/resources/tools-software/sigmastudio/toolbox");
        return
        [
            new CatalogBlockDto("io.audio_input", "Audio Input", "Hardware/Input", "I/O", [], CatalogCompatibility.Supported, [], [new PinDto(0, "Output", "output")], [], [], false, false, new Dictionary<string, double>(), [], new Dictionary<string, string>(), source, "ADAU1701 audio input endpoint."),
            new CatalogBlockDto("volume.linear_gain", "Linear Gain", "Volume/Linear Gain", "Volume", ["ADAU1701"], CatalogCompatibility.Supported, [new PinDto(0, "Input", "input")], [new PinDto(0, "Output", "output")], [new ControlDto("Gain", 0, -24, 24, null, "dB")], [], false, false, new Dictionary<string, double> { ["instructions"] = 8 }, [], new Dictionary<string, string> { ["insert"] = "runtime-discovery-required" }, source, "A documented linear gain control with a -24 dB to +24 dB range."),
            new CatalogBlockDto("filter.parametric_eq", "Parametric EQ", "Filters/Parametric EQ", "Filters", ["ADAU1701"], CatalogCompatibility.Supported, [new PinDto(0, "Input", "input")], [new PinDto(0, "Output", "output")], [new ControlDto("Frequency", 1000, 20, 20000, null, "Hz"), new ControlDto("Gain", 0, -24, 24, null, "dB"), new ControlDto("Q", 1, 0.1, 20, null, "Q")], [], false, false, new Dictionary<string, double> { ["instructions"] = 42 }, [], new Dictionary<string, string> { ["insert"] = "runtime-discovery-required" }, source, "Parametric equalizer metadata; exact installed toolbox automation name must be verified."),
            new CatalogBlockDto("io.audio_output", "Audio Output", "Hardware/Output", "I/O", [], CatalogCompatibility.Supported, [new PinDto(0, "Input", "input")], [], [], [], false, false, new Dictionary<string, double>(), [], new Dictionary<string, string>(), source, "ADAU1701 audio output endpoint.")
        ];
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
