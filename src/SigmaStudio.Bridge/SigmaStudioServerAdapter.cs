using System.Reflection;
using System.IO;
using System.Text.Json;
using SigmaStudio.Contracts;
using SigmaStudio.Graph;

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
    private ProjectGraphDto? _liveGraph;
    private readonly SigmaStudioExportReader _exportReader = new();
    private string? _lastCheckpointDirectory;

    public SigmaStudioServerAdapter(string? configuredPath) => _configuredPath = configuredPath;

    public string BackendName => IsLoaded ? "SigmaStudioServer" : "SigmaStudioServer-unavailable";
    public bool IsLoaded => _server is not null;
    public IReadOnlyList<string> Methods =>
    [
        "project.create", "project.open", "project.save", "project.saveAs", "project.close", "project.checkpoint",
        "project.undo", "project.redo", "project.export", "graph.link", "graph.compile", "graph.download",
        "graph.refreshLive",
        "catalog.discovery",
        "block.add", "block.remove", "block.rename", "block.getControls", "block.setControl", "block.setControls", "connection.add", "connection.remove"
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
                .ToArray(),
            signatures = serverType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => new[] { "GET_OBJECT_PROPERTY", "SET_OBJECT_PROPERTY", "INSERT_BLOCKOBJECT", "INSERT_BLOCKOBJECT_POINT", "INSERT_OBJECT", "INSERT_OBJECT_POINT", "CONNECT_OBJECT", "DISCONNECT_OBJECT" }.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
                .Select(m => new
                {
                    name = m.Name,
                    parameters = m.GetParameters().Select(p => new { p.Name, type = p.ParameterType.FullName }).ToArray(),
                    returnType = m.ReturnType.FullName
                })
                .ToArray()
        };
    }

    public AutomationSnapshot GetSnapshot()
    {
        var projectState = new ProjectStateDto(
            _projectPath,
            _projectPath is null ? null : Path.GetFileNameWithoutExtension(_projectPath),
            _liveGraph?.Project.Chip ?? "ADAU1701",
            _liveGraph?.Project.SampleRateHz ?? 48000,
            _dirty,
            _designRevision,
            _runtimeRevision,
            _deployedDesignRevision,
            _state,
            _state == SigmaStudioState.ActiveDownloaded && _deployedDesignRevision == _designRevision);
        var graph = _liveGraph ?? new ProjectGraphDto(
            new ProjectIdentityDto(_projectPath, _liveGraph?.Project.Chip ?? "ADAU1701", _liveGraph?.Project.SampleRateHz ?? 48000),
            [],
            [],
            _designRevision,
            GraphFreshness.Cached,
            ["Live graph is not loaded. Call sigma_graph_get with refresh=live."]);
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
                    return ExportAndRefresh(RequiredString(payload, "path"));
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
                case "graph.refreshLive":
                    return ExportLiveGraph();
                case "block.getControls":
                    return RefreshBlockControls(RequiredString(payload, "block"));
                case "catalog.discovery":
                    return CatalogDiscovery();
                case "block.remove":
                    return Invoke(method, "REMOVE_OBJECT", RequiredString(payload, "block"));
                case "connection.add":
                    var add = RequiredConnection(payload);
                    return Invoke(method, "CONNECT_OBJECT",
                        add.SourceBlock, add.SourcePinIndex, add.TargetBlock, add.TargetPinIndex);
                case "connection.remove":
                    var remove = RequiredConnection(payload);
                    return Invoke(method, "DISCONNECT_OBJECT",
                        remove.SourceBlock, remove.SourcePinIndex, remove.TargetBlock, remove.TargetPinIndex);
                case "project.checkpoint":
                    return CreateCheckpoint();
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
                _liveGraph = null;
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
                _liveGraph = null;
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
                if (_liveGraph is not null) _liveGraph = _liveGraph with { Freshness = GraphFreshness.Stale, Warnings = [.. (_liveGraph.Warnings ?? []), "Graph changed; refreshLive is required to re-read SigmaStudio."] };
                break;
        }
    }

    private AdapterResult ExportAndRefresh(string exportPath)
    {
        var result = Invoke("project.export", "EXPORT_SYSTEM_FILES", exportPath);
        if (!result.Ok) return result;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(exportPath));
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("The export path has no directory.");
            _liveGraph = _exportReader.Read(directory, _projectPath, _designRevision).Graph;
            return AdapterResult.Success(new { exportPath, graph = _liveGraph });
        }
        catch (Exception ex)
        {
            return AdapterResult.Failure("GRAPH_EXTRACTION_FAILED", $"SigmaStudio export completed but graph extraction failed: {ex.Message}");
        }
    }

    private AdapterResult ExportLiveGraph()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sigmastudio-mcp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var exportPath = Path.Combine(directory, "live-graph");
        try
        {
            var result = Invoke("project.export", "EXPORT_SYSTEM_FILES", exportPath);
            if (!result.Ok) return result;
            _liveGraph = _exportReader.Read(directory, _projectPath, _designRevision).Graph;
            return AdapterResult.Success(new { graph = _liveGraph, exportPath });
        }
        catch (Exception ex)
        {
            return AdapterResult.Failure("GRAPH_EXTRACTION_FAILED", $"Live SigmaStudio graph extraction failed: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private AdapterResult CreateCheckpoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sigmastudio-mcp", "checkpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var exportPath = Path.Combine(directory, "checkpoint");
        var result = Invoke("project.export", "EXPORT_SYSTEM_FILES", exportPath);
        if (!result.Ok)
        {
            try { Directory.Delete(directory, true); } catch { }
            return result;
        }
        if (_lastCheckpointDirectory is not null && Directory.Exists(_lastCheckpointDirectory))
        {
            try { Directory.Delete(_lastCheckpointDirectory, true); } catch { }
        }
        _lastCheckpointDirectory = directory;
        return AdapterResult.Success(new { path = directory, exportPath, rollback = "project.undo" });
    }

    private AdapterResult CatalogDiscovery()
    {
        EnsureLoaded();
        var runtimeMethods = _server!.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Select(method => method.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var verified = new[] { "EXPORT_SYSTEM_FILES", "INSERT_BLOCKOBJECT", "INSERT_BLOCKOBJECT_POINT", "REMOVE_OBJECT", "CONNECT_OBJECT", "DISCONNECT_OBJECT" }
            .Where(runtimeMethods.Contains).ToArray();
        return AdapterResult.Success(new CatalogDiscoveryDto(
            false,
            "SigmaStudioServer public method surface",
            verified,
            ["The 4.7 server API does not expose a verified installed Toolbox enumeration. Catalog availability remains Wiki metadata plus per-block runtime verification."]));
    }

    private AdapterResult RefreshBlockControls(string blockName)
    {
        var export = ExportLiveGraph();
        if (!export.Ok || _liveGraph is null) return export;
        var block = _liveGraph.Blocks.FirstOrDefault(candidate => string.Equals(candidate.Id, blockName, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.ObjectName, blockName, StringComparison.OrdinalIgnoreCase));
        if (block is null) return AdapterResult.Failure("BLOCK_NOT_FOUND", $"Block '{blockName}' was not found in the live graph.");

        var readCount = 0;
        var controls = block.Controls.Select(control =>
        {
            if (!TryGetControlValue(block.ObjectName, control.Name, out var value)) return control;
            readCount++;
            return control with { Value = value, Source = "live-sigmastudio-property", Freshness = GraphFreshness.Fresh };
        }).ToArray();
        var warnings = (_liveGraph.Warnings ?? []).ToList();
        if (readCount == 0) warnings.Add($"CONTROL_READBACK_UNAVAILABLE: GET_OBJECT_PROPERTY did not return a value for '{block.ObjectName}'.");
        _liveGraph = GraphIdentity.WithIdentity(_liveGraph with
        {
            Blocks = _liveGraph.Blocks.Select(candidate => candidate.Id == block.Id ? candidate with { Controls = controls } : candidate).ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        });
        return AdapterResult.Success(new { block = block.ObjectName, readCount, controls });
    }

    private bool TryGetControlValue(string objectName, string controlName, out double? value)
    {
        value = null;
        var method = _server!.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, "GET_OBJECT_PROPERTY", StringComparison.OrdinalIgnoreCase) && candidate.GetParameters().Length == 4);
        if (method is null) return false;
        var arguments = new object?[] { "getControlValue", objectName, Array.Empty<object>(), new object[] { controlName } };
        try
        {
            var returned = method.Invoke(_server, arguments);
            if (returned is bool success && !success) return false;
            if (arguments[2] is not object[] values || values.Length == 0 || values[0] is null) return false;
            value = values[0] switch
            {
                bool boolean => boolean ? 1 : 0,
                byte number => number,
                short number => number,
                int number => number,
                long number => number,
                float number => number,
                double number => number,
                decimal number => (double)number,
                _ when double.TryParse(values[0].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            };
            return value is not null;
        }
        catch
        {
            return false;
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

    private static ConnectionEndpoints RequiredConnection(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("Connection parameters must be an object.");
        var source = payload.TryGetProperty("source", out var nestedSource) ? nestedSource : payload;
        var target = payload.TryGetProperty("target", out var nestedTarget) ? nestedTarget : payload;
        var sourceBlock = nestedSource.ValueKind != JsonValueKind.Undefined
            ? RequiredString(source, "block")
            : RequiredString(payload, "sourceBlock");
        var targetBlock = nestedTarget.ValueKind != JsonValueKind.Undefined
            ? RequiredString(target, "block")
            : RequiredString(payload, "targetBlock");
        var sourcePinIndex = nestedSource.ValueKind != JsonValueKind.Undefined
            ? RequiredInt(source, "pinIndex")
            : RequiredInt(payload, "sourcePinIndex");
        var targetPinIndex = nestedTarget.ValueKind != JsonValueKind.Undefined
            ? RequiredInt(target, "pinIndex")
            : RequiredInt(payload, "targetPinIndex");
        return new ConnectionEndpoints(sourceBlock, sourcePinIndex, targetBlock, targetPinIndex);
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

    private sealed record ConnectionEndpoints(string SourceBlock, int SourcePinIndex, string TargetBlock, int TargetPinIndex);
}

public sealed record AdapterResult(bool Ok, object? Result = null, BridgeError? Error = null)
{
    public static AdapterResult Success(object? result = null) => new(true, result, null);
    public static AdapterResult Failure(string code, string message) => new(false, null, new BridgeError(code, message));
}
