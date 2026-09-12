using System.Text.Json;
using SigmaStudio.Catalog;
using SigmaStudio.Core;
using SigmaStudio.Graph;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "doctor";
var options = new SigmaStudioOptions();
var catalog = new CatalogStore();
var runtime = new SigmaRuntime(new InMemorySigmaStudioAutomation(), new ProjectPathPolicy(options), new GraphValidator(), () => catalog.Blocks, options);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

switch (command)
{
    case "doctor":
        Console.WriteLine(JsonSerializer.Serialize(await runtime.DiagnosticsAsync(CancellationToken.None), jsonOptions));
        break;
    case "ui-dump":
        Console.WriteLine("UI dump is provided by SigmaStudio.Bridge after a verified SigmaStudio UI Automation profile is configured.");
        break;
    default:
        Console.Error.WriteLine("Usage: sigmastudio-mcp [doctor|ui-dump]");
        return 2;
}

return 0;
