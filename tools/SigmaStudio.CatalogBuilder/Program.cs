using System.Text.Json;
using System.Text.Json.Serialization;
using SigmaStudio.Catalog;
using SigmaStudio.Contracts;

var input = Option(args, "--input");
var output = Option(args, "--output");
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true, WriteIndented = true };
jsonOptions.Converters.Add(new JsonStringEnumConverter());
if (input is null || output is null)
{
    Console.Error.WriteLine("Usage: SigmaStudio.CatalogBuilder --input <catalog.json> --output <catalog.json>");
    return 2;
}

var blocks = JsonSerializer.Deserialize<List<CatalogBlockDto>>(await File.ReadAllTextAsync(input), jsonOptions) ?? [];
var issues = new CatalogValidator().Validate(blocks);
if (issues.Count > 0)
{
    foreach (var issue in issues) Console.Error.WriteLine(issue);
    return 1;
}

var directory = Path.GetDirectoryName(Path.GetFullPath(output));
if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(blocks, jsonOptions));
Console.WriteLine($"Validated {blocks.Count} catalog blocks -> {output}");
return 0;

static string? Option(string[] args, string name)
{
    var prefix = name + "=";
    return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..].Trim('"');
}
