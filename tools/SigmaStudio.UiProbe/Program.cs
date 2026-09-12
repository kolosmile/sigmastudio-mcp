using System.IO;
using System.Text.Json;
using System.Windows.Automation;

var output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.OrdinalIgnoreCase))?[9..];
var root = AutomationElement.RootElement;
var sigmaWindows = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))
    .Cast<AutomationElement>()
    .Where(window => (window.Current.Name ?? "").Contains("SigmaStudio", StringComparison.OrdinalIgnoreCase))
    .Select(BuildNode)
    .ToArray();
var result = new
{
    capturedAt = DateTimeOffset.UtcNow,
    process = "SStudio",
    windows = sigmaWindows
};
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
if (string.IsNullOrWhiteSpace(output)) Console.WriteLine(json);
else await File.WriteAllTextAsync(output, json);

static object BuildNode(AutomationElement element)
{
    var children = element.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>().Take(500).Select(BuildNode).ToArray();
    return new
    {
        name = element.Current.Name,
        automationId = element.Current.AutomationId,
        controlType = element.Current.ControlType.ProgrammaticName,
        className = element.Current.ClassName,
        children
    };
}
