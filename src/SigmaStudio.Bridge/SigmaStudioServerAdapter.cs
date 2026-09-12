using System.Reflection;
using System.IO;
using System.Text.Json;
using SigmaStudio.Contracts;

namespace SigmaStudio.Bridge;

public sealed class SigmaStudioServerAdapter
{
    private readonly string? _configuredPath;
    private Assembly? _assembly;
    private object? _server;

    public SigmaStudioServerAdapter(string? configuredPath) => _configuredPath = configuredPath;

    public string BackendName => IsLoaded ? "SigmaStudioServer" : "SigmaStudioServer-unavailable";
    public bool IsLoaded => _server is not null;
    public IReadOnlyList<string> Methods =>
    [
        "project.create", "project.open", "project.save", "project.saveAs", "project.close", "project.checkpoint",
        "project.undo", "project.redo", "project.export", "graph.link", "graph.compile", "graph.download",
        "block.add", "block.remove", "block.rename", "block.setControl", "block.setControls", "connection.add", "connection.remove"
    ];

    public object GetSnapshot() => new
    {
        state = SigmaStudioState.NoProject,
        graph = new ProjectGraphDto(new ProjectIdentityDto(null, "ADAU1701", 48000), [], [], 0, GraphFreshness.Cached),
        capture = Array.Empty<CaptureEntryDto>(),
        backend = BackendName,
        connected = IsLoaded
    };

    public AdapterResult Execute(string method, JsonElement? parameters)
    {
        EnsureLoaded();
        // The exact ADI method signatures are version-specific. This adapter intentionally
        // exposes only reflection-discovered methods and never invents SigmaStudio object names.
        var methodName = MethodName(method);
        var candidates = _server!.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) return AdapterResult.Failure("SIGMASTUDIO_SERVER_CONNECTION_FAILED", $"No runtime method named '{methodName}' was discovered in the installed SigmaStudioServer assembly.");
        return AdapterResult.Failure("SIGMASTUDIO_SERVER_CONNECTION_FAILED", $"Runtime invocation for '{methodName}' requires a verified SigmaStudio 4.7 signature; run the probe before enabling production automation.");
    }

    private void EnsureLoaded()
    {
        if (IsLoaded) return;
        var path = ResolvePath();
        if (path is null) throw new InvalidOperationException("SIGMASTUDIO_SERVER_DLL_NOT_FOUND: Configure SigmaStudioServerPath or install SigmaStudio 4.7.");
        _assembly = Assembly.LoadFrom(path);
        var type = _assembly.GetType("Analog.SigmaStudioServer.SigmaStudioServer", throwOnError: false);
        if (type is null) throw new InvalidOperationException("SIGMASTUDIO_SERVER_CONNECTION_FAILED: SigmaStudioServer type was not found.");
        _server = Activator.CreateInstance(type);
    }

    private string? ResolvePath()
    {
        if (!string.IsNullOrWhiteSpace(_configuredPath) && File.Exists(_configuredPath)) return _configuredPath;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(p => !string.IsNullOrWhiteSpace(p));
        return roots.SelectMany(root => Directory.Exists(root) ? Directory.EnumerateFiles(root, "Analog.SigmaStudioServer.dll", SearchOption.AllDirectories) : []).FirstOrDefault();
    }

    private static string MethodName(string method) => method switch
    {
        "project.create" => "NEW_PROJECT",
        "project.open" => "OPEN_PROJECT",
        "project.save" => "SAVE_PROJECT",
        "project.saveAs" => "SAVE_PROJECT_AS",
        "project.close" => "CLOSE_PROJECT",
        "project.export" => "EXPORT_SYSTEM_FILES",
        "graph.link" => "LINK",
        "graph.compile" => "COMPILE",
        "graph.download" => "DOWNLOAD",
        "block.add" => "INSERT_BLOCK_OBJECT",
        "block.remove" => "REMOVE_OBJECT",
        "block.rename" => "SET_OBJECT_PROPERTY",
        "connection.add" => "CONNECT_OBJECT",
        "connection.remove" => "DISCONNECT_OBJECT",
        "block.setControl" or "block.setControls" => "SET_OBJECT_PROPERTY",
        _ => method
    };
}

public sealed record AdapterResult(bool Ok, object? Result = null, BridgeError? Error = null)
{
    public static AdapterResult Failure(string code, string message) => new(false, null, new BridgeError(code, message));
}
