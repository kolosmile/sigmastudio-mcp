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
    private string? _projectPath;
    private SigmaStudioState _state = SigmaStudioState.NoProject;
    private long _designRevision;
    private long _runtimeRevision;
    private long? _deployedDesignRevision;
    private bool _dirty;

    public SigmaStudioServerAdapter(string? configuredPath) => _configuredPath = configuredPath;

    public string BackendName => IsLoaded ? "SigmaStudioServer" : "SigmaStudioServer-unavailable";
    public bool IsLoaded => _server is not null;
    public IReadOnlyList<string> Methods =>
    [
        "project.create", "project.open", "project.save", "project.saveAs", "project.close", "project.checkpoint",
        "project.undo", "project.redo", "project.export", "graph.link", "graph.compile", "graph.download",
        "block.add", "block.remove", "block.rename", "block.setControl", "block.setControls", "connection.add", "connection.remove"
    ];

    public object Probe()
    {
        EnsureLoaded();
        var serverType = _server!.GetType();
        return new
        {
            loaded = true,
            assembly = _assembly!.FullName,
            type = serverType.FullName,
            methods = serverType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Select(m => m.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public AutomationSnapshot GetSnapshot()
    {
        var projectState = new ProjectStateDto(
            _projectPath,
            _projectPath is null ? null : Path.GetFileNameWithoutExtension(_projectPath),
            "ADAU1701",
            48000,
            _dirty,
            _designRevision,
            _runtimeRevision,
            _deployedDesignRevision,
            _state,
            _state == SigmaStudioState.ActiveDownloaded && _deployedDesignRevision == _designRevision);
        var graph = new ProjectGraphDto(
            new ProjectIdentityDto(_projectPath, "ADAU1701", 48000),
            [],
            [],
            _designRevision,
            GraphFreshness.Cached,
            ["Live SigmaStudio graph extraction is not yet available from the 4.7 server API."]);
        return new AutomationSnapshot(projectState, graph, [], BackendName, IsLoaded);
    }

    public AdapterResult Execute(string method, JsonElement? parameters)
    {
        try
        {
            EnsureLoaded();
            var payload = parameters ?? default;
            switch (method)
            {
                case "project.create":
                    return Invoke(method, "NEW_PROJECT");
                case "project.open":
                    return Invoke(method, "OPEN_PROJECT", RequiredString(payload, "path"));
                case "project.save":
                    return Invoke(method, "SAVE_PROJECT");
                case "project.saveAs":
                    return Invoke(method, "SAVEAS_PROJECT", RequiredString(payload, "path"));
                case "project.close":
                    return Invoke(method, "CLOSE_PROJECT");
                case "project.export":
                    return Invoke(method, "EXPORT_SYSTEM_FILES", RequiredString(payload, "path"));
                case "project.undo":
                    return Invoke(method, "UNDO_SCRIPT");
                case "project.redo":
                    return Invoke(method, "REDO_SCRIPT");
                case "graph.link":
                    return Invoke(method, "LINK");
                case "graph.compile":
                    return Invoke(method, "COMPILE");
                case "graph.download":
                    return Invoke(method, "DOWNLOAD");
                case "block.remove":
                    return Invoke(method, "REMOVE_OBJECT", RequiredString(payload, "block"));
                case "connection.add":
                    return Invoke(method, "CONNECT_OBJECT",
                        RequiredString(payload, "sourceBlock"), RequiredInt(payload, "sourcePinIndex"),
                        RequiredString(payload, "targetBlock"), RequiredInt(payload, "targetPinIndex"));
                case "connection.remove":
                    return Invoke(method, "DISCONNECT_OBJECT",
                        RequiredString(payload, "sourceBlock"), RequiredInt(payload, "sourcePinIndex"),
                        RequiredString(payload, "targetBlock"), RequiredInt(payload, "targetPinIndex"));
                case "project.checkpoint":
                case "block.add":
                case "block.rename":
                case "block.setControl":
                case "block.setControls":
                    return AdapterResult.Failure("AUTOMATION_UNVERIFIED", $"The SigmaStudio 4.7 server API does not expose a verified mapping for '{method}'. No guessed object or property name was used.");
                default:
                    return AdapterResult.Failure("AUTOMATION_UNSUPPORTED", $"The SigmaStudio bridge does not implement '{method}'.");
            }
        }
        catch (ArgumentException ex)
        {
            return AdapterResult.Failure("INVALID_PARAMETERS", ex.Message);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return AdapterResult.Failure("SIGMASTUDIO_OPERATION_FAILED", ex.InnerException.Message);
        }
        catch (Exception ex)
        {
            return AdapterResult.Failure("SIGMASTUDIO_SERVER_CONNECTION_FAILED", ex.Message);
        }
    }

    private AdapterResult Invoke(string operation, string methodName, params object?[] args)
    {
        var method = _server!.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(m => m.GetParameters().Length == args.Length);
        if (method is null) return AdapterResult.Failure("SIGMASTUDIO_SERVER_CONNECTION_FAILED", $"No verified runtime method named '{methodName}' with {args.Length} parameter(s) was discovered.");

        var returned = method.Invoke(_server, args);
        if (returned is bool ok && !ok) return AdapterResult.Failure("SIGMASTUDIO_OPERATION_FAILED", $"SigmaStudio returned false for '{methodName}'.");
        ApplyState(operation, args);
        return AdapterResult.Success(new { operation, method = methodName, returned });
    }

    private void ApplyState(string operation, object?[] args)
    {
        switch (operation)
        {
            case "project.create":
                _projectPath = null;
                _state = SigmaStudioState.DesignMode;
                _designRevision++;
                _dirty = true;
                _deployedDesignRevision = null;
                break;
            case "project.open":
                _projectPath = args.Length == 1 ? args[0] as string : null;
                _state = SigmaStudioState.DesignMode;
                _designRevision = Math.Max(1, _designRevision + 1);
                _runtimeRevision = 0;
                _dirty = false;
                _deployedDesignRevision = null;
                break;
            case "project.save":
            case "project.saveAs":
                if (operation == "project.saveAs" && args.Length == 1) _projectPath = args[0] as string;
                _dirty = false;
                break;
            case "project.close":
                _projectPath = null;
                _state = SigmaStudioState.NoProject;
                _dirty = false;
                _deployedDesignRevision = null;
                break;
            case "graph.link":
            case "graph.compile":
                _state = SigmaStudioState.ReadyCompiled;
                break;
            case "graph.download":
                _state = SigmaStudioState.ActiveDownloaded;
                _deployedDesignRevision = _designRevision;
                break;
            case "block.remove":
            case "connection.add":
            case "connection.remove":
                _designRevision++;
                _dirty = true;
                _state = SigmaStudioState.DesignMode;
                _deployedDesignRevision = null;
                break;
        }
    }

    private static string RequiredString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"Parameter '{name}' is required.");
        return value.GetString()!;
    }

    private static int RequiredInt(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new ArgumentException($"Integer parameter '{name}' is required.");
        return result;
    }

    private void EnsureLoaded()
    {
        if (IsLoaded) return;
        var path = ResolvePath();
        if (path is null) throw new InvalidOperationException("SIGMASTUDIO_SERVER_DLL_NOT_FOUND: Configure SigmaStudioServerPath or install SigmaStudio 4.7.");
        _assembly = Assembly.LoadFrom(path);
        var type = _assembly.GetType("Analog.SigmaStudioServer.SigmaStudioServer", throwOnError: false);
        if (type is null) throw new InvalidOperationException("SIGMASTUDIO_SERVER_CONNECTION_FAILED: SigmaStudioServer type was not found.");
        try
        {
            _server = Activator.CreateInstance(type);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new InvalidOperationException($"SIGMASTUDIO_SERVER_CONNECTION_FAILED: {ex.InnerException.Message}", ex.InnerException);
        }
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
        "project.saveAs" => "SAVEAS_PROJECT",
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
    public static AdapterResult Success(object? result = null) => new(true, result, null);
    public static AdapterResult Failure(string code, string message) => new(false, null, new BridgeError(code, message));
}
