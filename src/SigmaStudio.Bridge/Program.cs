using SigmaStudio.Bridge;

var options = BridgeOptions.Parse(args);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var adapter = new SigmaStudioServerAdapter(options.SigmaStudioServerPath);
var observer = new UiAutomationObserver();
var server = new NamedPipeBridgeServer(options, adapter, observer);
Console.WriteLine($"SigmaStudio Bridge listening on {options.PipeName}");
await server.RunAsync(cts.Token);
