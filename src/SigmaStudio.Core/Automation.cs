using System.Collections.Concurrent;
using System.Text.Json;
using SigmaStudio.Contracts;

namespace SigmaStudio.Core;

public sealed record AutomationCommand(string Name, object? Parameters = null);

public sealed record AutomationResult(bool Ok, object? Data = null, string? ErrorCode = null, string? ErrorMessage = null, IReadOnlyDictionary<string, object?>? Details = null)
{
    public static AutomationResult Success(object? data = null) => new(true, data);
    public static AutomationResult Failure(string code, string message, IReadOnlyDictionary<string, object?>? details = null) => new(false, null, code, message, details);
}

public interface ISigmaStudioAutomation
{
    string BackendName { get; }
    Task<AutomationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<AutomationResult> ExecuteAsync(AutomationCommand command, CancellationToken cancellationToken);
}

public sealed class InMemorySigmaStudioAutomation : ISigmaStudioAutomation
{
    private readonly object _gate = new();
    private readonly Stack<ProjectGraphDto> _undo = new();
    private readonly Stack<ProjectGraphDto> _redo = new();
    private readonly List<CaptureEntryDto> _capture = [];
    private ProjectGraphDto _graph = EmptyGraph();
    private string? _path;
    private string? _name;
    private int _sampleRate = 48000;
    private bool _dirty;
    private SigmaStudioState _state = SigmaStudioState.NoProject;
    private long _designRevision;
    private long _runtimeRevision;
    private long? _deployedDesignRevision;
    private long _captureSequence;

    public string BackendName => "InMemory";

    public Task<AutomationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(Snapshot());
    }

    public Task<AutomationResult> ExecuteAsync(AutomationCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            try
            {
                return Task.FromResult(ExecuteLocked(command));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AutomationResult.Failure("INTERNAL_ERROR", ex.Message));
            }
        }
    }

    private AutomationResult ExecuteLocked(AutomationCommand command)
    {
        return command.Name switch
        {
            "project.create" => Create(command.Parameters),
            "project.open" => Open(command.Parameters),
            "project.save" => Save(),
            "project.saveAs" => SaveAs(command.Parameters),
            "project.close" => Close(),
            "project.checkpoint" => Checkpoint(),
            "project.undo" => Undo(),
            "project.redo" => Redo(),
            "project.export" => AutomationResult.Success(_graph),
            "graph.refreshLive" => AutomationResult.Success(_graph with { Freshness = GraphFreshness.Fresh }),
            "catalog.discovery" => AutomationResult.Success(new CatalogDiscoveryDto(false, "in-memory", [], ["Installed SigmaStudio Toolbox is unavailable in the in-memory backend."])),
            "graph.link" => Build(SigmaStudioState.ReadyCompiled),
            "graph.compile" => Build(SigmaStudioState.ReadyCompiled),
            "graph.download" => Download(),
            "block.add" => AddBlock(command.Parameters),
            "block.remove" => RemoveBlock(command.Parameters),
            "block.rename" => RenameBlock(command.Parameters),
            "block.setControl" => SetControl(command.Parameters),
            "block.setControls" => SetControls(command.Parameters),
            "connection.add" => AddConnection(command.Parameters),
            "connection.remove" => RemoveConnection(command.Parameters),
            _ => AutomationResult.Failure("INTERNAL_ERROR", $"InMemory backend does not implement '{command.Name}'.")
        };
    }

    private AutomationResult Create(object? parameters)
    {
        var input = Deserialize<ProjectCreateInput>(parameters);
        _path = input.Path;
        _name = Path.GetFileNameWithoutExtension(_path);
        _sampleRate = input.SampleRateHz;
        _graph = DefaultGraph(_path, _sampleRate);
        _designRevision = 1;
        _runtimeRevision = 0;
        _deployedDesignRevision = null;
        _dirty = true;
        _state = SigmaStudioState.DesignMode;
        _undo.Clear();
        _redo.Clear();
        return AutomationResult.Success(_graph);
    }

    private AutomationResult Open(object? parameters)
    {
        var input = Deserialize<ProjectOpenInput>(parameters);
        _path = input.Path;
        _name = Path.GetFileNameWithoutExtension(_path);
        _sampleRate = 48000;
        _graph = DefaultGraph(_path, _sampleRate);
        _designRevision = 1;
        _runtimeRevision = 0;
        _deployedDesignRevision = null;
        _dirty = false;
        _state = SigmaStudioState.DesignMode;
        return AutomationResult.Success(_graph);
    }

    private AutomationResult Save()
    {
        if (_path is null) return AutomationResult.Failure("PROJECT_NOT_OPEN", "No project is open.");
        PersistSidecar(_path);
        _dirty = false;
        return AutomationResult.Success(new { path = _path });
    }

    private AutomationResult SaveAs(object? parameters)
    {
        var input = Deserialize<ProjectSaveAsInput>(parameters);
        if (File.Exists(input.Path) && !input.Overwrite) return AutomationResult.Failure("PROJECT_SAVE_FAILED", "Target already exists; set overwrite=true to replace it.");
        _path = input.Path;
        _name = Path.GetFileNameWithoutExtension(_path);
        PersistSidecar(_path);
        _dirty = false;
        return AutomationResult.Success(new { path = _path });
    }

    private AutomationResult Close()
    {
        if (_dirty) return AutomationResult.Failure("PROJECT_DIRTY", "Project has unsaved changes.");
        _path = null;
        _name = null;
        _graph = EmptyGraph();
        _state = SigmaStudioState.NoProject;
        _deployedDesignRevision = null;
        return AutomationResult.Success();
    }

    private AutomationResult Checkpoint()
    {
        if (_path is null) return AutomationResult.Failure("PROJECT_NOT_OPEN", "No project is open.");
        var directory = Path.Combine(Path.GetDirectoryName(_path) ?? ".", ".sigmastudio-mcp", "backups");
        Directory.CreateDirectory(directory);
        var checkpointPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(_path)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json");
        File.WriteAllText(checkpointPath, JsonSerializer.Serialize(_graph, JsonOptions));
        return AutomationResult.Success(new { path = checkpointPath });
    }

    private AutomationResult Undo()
    {
        if (_undo.Count == 0) return AutomationResult.Failure("INTERNAL_ERROR", "Nothing to undo.");
        _redo.Push(_graph);
        _graph = _undo.Pop() with { DesignRevision = ++_designRevision, Freshness = GraphFreshness.Stale };
        _dirty = true;
        _state = SigmaStudioState.DesignMode;
        return AutomationResult.Success(_graph);
    }

    private AutomationResult Redo()
    {
        if (_redo.Count == 0) return AutomationResult.Failure("INTERNAL_ERROR", "Nothing to redo.");
        _undo.Push(_graph);
        _graph = _redo.Pop() with { DesignRevision = ++_designRevision, Freshness = GraphFreshness.Stale };
        _dirty = true;
        _state = SigmaStudioState.DesignMode;
        return AutomationResult.Success(_graph);
    }

    private AutomationResult Build(SigmaStudioState state)
    {
        if (_path is null) return AutomationResult.Failure("PROJECT_NOT_OPEN", "No project is open.");
        _state = state;
        _graph = _graph with { Freshness = GraphFreshness.Fresh };
        return AutomationResult.Success(new { state = _state.ToString() });
    }

    private AutomationResult Download()
    {
        if (_path is null) return AutomationResult.Failure("PROJECT_NOT_OPEN", "No project is open.");
        _state = SigmaStudioState.ActiveDownloaded;
        _deployedDesignRevision = _designRevision;
        _graph = _graph with { Freshness = GraphFreshness.Fresh };
        AddCapture("download", "DSP", "Download completed");
        return AutomationResult.Success(new { state = _state.ToString() });
    }

    private AutomationResult AddBlock(object? parameters)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(parameters, JsonOptions));
        var catalog = JsonSerializer.Deserialize<CatalogBlockDto>(payload.GetProperty("catalog").GetRawText(), JsonOptions) ?? throw new InvalidOperationException("Missing catalog block.");
        var objectName = payload.GetProperty("objectName").GetString() ?? catalog.Name;
        SaveUndo();
        var block = new BlockDto(
            $"block-{Guid.NewGuid():N}", objectName, objectName, catalog.Id, catalog.Name,
            [], catalog.Inputs, catalog.Outputs, catalog.Controls, catalog.DspParameters,
            payload.TryGetProperty("x", out var x) && payload.TryGetProperty("y", out var y) ? new GraphPositionDto(x.GetDouble(), y.GetDouble()) : null);
        _graph = _graph with { Blocks = [.. _graph.Blocks, block], DesignRevision = ++_designRevision, Freshness = GraphFreshness.Stale };
        MarkDesignChanged();
        return AutomationResult.Success(block);
    }

    private AutomationResult RemoveBlock(object? parameters)
    {
        var blockName = GetString(parameters, "block");
        var block = _graph.Blocks.FirstOrDefault(b => string.Equals(b.ObjectName, blockName, StringComparison.OrdinalIgnoreCase));
        if (block is null) return AutomationResult.Failure("BLOCK_NOT_FOUND", $"Block '{blockName}' was not found.");
        SaveUndo();
        _graph = _graph with
        {
            Blocks = _graph.Blocks.Where(b => !string.Equals(b.ObjectName, blockName, StringComparison.OrdinalIgnoreCase)).ToArray(),
            Connections = _graph.Connections.Where(c => !string.Equals(c.Source.Block, blockName, StringComparison.OrdinalIgnoreCase) && !string.Equals(c.Target.Block, blockName, StringComparison.OrdinalIgnoreCase)).ToArray(),
            DesignRevision = ++_designRevision,
            Freshness = GraphFreshness.Stale
        };
        MarkDesignChanged();
        return AutomationResult.Success(new { removed = blockName });
    }

    private AutomationResult RenameBlock(object? parameters)
    {
        var oldName = GetString(parameters, "block");
        var newName = GetString(parameters, "newName");
        if (_graph.Blocks.Any(b => string.Equals(b.ObjectName, newName, StringComparison.OrdinalIgnoreCase))) return AutomationResult.Failure("INTERNAL_ERROR", $"Block '{newName}' already exists.");
        if (_graph.Blocks.All(b => !string.Equals(b.ObjectName, oldName, StringComparison.OrdinalIgnoreCase))) return AutomationResult.Failure("BLOCK_NOT_FOUND", $"Block '{oldName}' was not found.");
        SaveUndo();
        _graph = _graph with
        {
            Blocks = _graph.Blocks.Select(b => string.Equals(b.ObjectName, oldName, StringComparison.OrdinalIgnoreCase) ? b with { ObjectName = newName, FullObjectName = newName } : b).ToArray(),
            Connections = _graph.Connections.Select(c => c with
            {
                Source = string.Equals(c.Source.Block, oldName, StringComparison.OrdinalIgnoreCase) ? c.Source with { Block = newName } : c.Source,
                Target = string.Equals(c.Target.Block, oldName, StringComparison.OrdinalIgnoreCase) ? c.Target with { Block = newName } : c.Target
            }).ToArray(),
            DesignRevision = ++_designRevision,
            Freshness = GraphFreshness.Stale
        };
        MarkDesignChanged();
        return AutomationResult.Success(new { oldName, newName });
    }

    private AutomationResult SetControl(object? parameters) => SetControls(new
    {
        block = GetString(parameters, "block"),
        changes = new[] { new ControlChangeInput(GetString(parameters, "control"), GetDouble(parameters, "value")) }
    });

    private AutomationResult SetControls(object? parameters)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(parameters, JsonOptions));
        var blockName = payload.GetProperty("block").GetString() ?? "";
        var block = _graph.Blocks.FirstOrDefault(b => string.Equals(b.ObjectName, blockName, StringComparison.OrdinalIgnoreCase));
        if (block is null) return AutomationResult.Failure("BLOCK_NOT_FOUND", $"Block '{blockName}' was not found.");
        var changes = JsonSerializer.Deserialize<List<ControlChangeInput>>(payload.GetProperty("changes").GetRawText(), JsonOptions) ?? [];
        var controls = block.Controls.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes)
        {
            if (!controls.TryGetValue(change.Control, out var control)) return AutomationResult.Failure("CONTROL_NOT_FOUND", $"Control '{change.Control}' was not found.");
            if (control.Min is not null && change.Value < control.Min || control.Max is not null && change.Value > control.Max) return AutomationResult.Failure("CONTROL_VALUE_OUT_OF_RANGE", $"Value for '{change.Control}' is outside its documented range.");
        }
        foreach (var change in changes)
        {
            controls[change.Control] = controls[change.Control] with { Value = change.Value };
            AddCapture("write", "CONTROL", $"{blockName}.{change.Control} = {change.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}", blockName, change.Control);
        }
        _graph = _graph with { Blocks = _graph.Blocks.Select(b => b == block ? b with { Controls = controls.Values.OrderBy(c => c.Name).ToArray() } : b).ToArray() };
        _runtimeRevision++;
        if (_state == SigmaStudioState.DesignMode && _deployedDesignRevision == _designRevision) _state = SigmaStudioState.ActiveDownloaded;
        return AutomationResult.Success(new { block = blockName, changes });
    }

    private AutomationResult AddConnection(object? parameters)
    {
        var connection = JsonSerializer.Deserialize<ConnectionDto>(JsonSerializer.Serialize(parameters, JsonOptions), JsonOptions) ?? throw new InvalidOperationException("Invalid connection.");
        if (_graph.Connections.Contains(connection)) return AutomationResult.Success(new { alreadyExists = true });
        if (!_graph.Blocks.Any(b => string.Equals(b.ObjectName, connection.Source.Block, StringComparison.OrdinalIgnoreCase)) || !_graph.Blocks.Any(b => string.Equals(b.ObjectName, connection.Target.Block, StringComparison.OrdinalIgnoreCase))) return AutomationResult.Failure("BLOCK_NOT_FOUND", "Connection block does not exist.");
        SaveUndo();
        _graph = _graph with { Connections = [.. _graph.Connections, connection], DesignRevision = ++_designRevision, Freshness = GraphFreshness.Stale };
        MarkDesignChanged();
        return AutomationResult.Success(connection);
    }

    private AutomationResult RemoveConnection(object? parameters)
    {
        var connection = JsonSerializer.Deserialize<ConnectionDto>(JsonSerializer.Serialize(parameters, JsonOptions), JsonOptions) ?? throw new InvalidOperationException("Invalid connection.");
        SaveUndo();
        var remaining = _graph.Connections.Where(c => c != connection).ToArray();
        if (remaining.Length == _graph.Connections.Count) return AutomationResult.Failure("CONNECTION_INVALID", "Connection was not found.");
        _graph = _graph with { Connections = remaining, DesignRevision = ++_designRevision, Freshness = GraphFreshness.Stale };
        MarkDesignChanged();
        return AutomationResult.Success(new { removed = true });
    }

    private void SaveUndo()
    {
        _undo.Push(_graph);
        _redo.Clear();
    }

    private void MarkDesignChanged()
    {
        _dirty = true;
        _state = SigmaStudioState.DesignMode;
        _deployedDesignRevision = _deployedDesignRevision == _designRevision ? null : _deployedDesignRevision;
    }

    private void AddCapture(string direction, string category, string summary, string? block = null, string? control = null)
    {
        _capture.Add(new CaptureEntryDto(++_captureSequence, DateTimeOffset.UtcNow, direction, category, summary, block, control));
        if (_capture.Count > 1000) _capture.RemoveRange(0, _capture.Count - 1000);
    }

    private void PersistSidecar(string path)
    {
        var sidecar = path + ".mcp.json";
        var directory = Path.GetDirectoryName(sidecar);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(sidecar, JsonSerializer.Serialize(_graph, JsonOptions));
    }

    private AutomationSnapshot Snapshot()
    {
        var ready = _state == SigmaStudioState.ActiveDownloaded && _deployedDesignRevision == _designRevision;
        var state = new ProjectStateDto(_path, _name, "ADAU1701", _sampleRate, _dirty, _designRevision, _runtimeRevision, _deployedDesignRevision, _state, ready);
        return new AutomationSnapshot(state, _graph with { DesignRevision = _designRevision }, _capture.ToArray(), BackendName, true);
    }

    private static ProjectGraphDto EmptyGraph() => new(new ProjectIdentityDto(null, "ADAU1701", 48000), [], [], 0, GraphFreshness.Cached);

    private static ProjectGraphDto DefaultGraph(string? path, int sampleRate) => new(
        new ProjectIdentityDto(path, "ADAU1701", sampleRate),
        [
            new BlockDto("input", "Input1", "Input1", "io.audio_input", "Audio Input", [], [], [new PinDto(0, "Output", "output")], [], [], new GraphPositionDto(0, 0)),
            new BlockDto("gain", "Gain1", "Gain1", "volume.linear_gain", "Linear Gain", [], [new PinDto(0, "Input", "input")], [new PinDto(0, "Output", "output")], [new ControlDto("Gain", 0, -24, 24, null, "dB")], [], new GraphPositionDto(180, 0)),
            new BlockDto("output", "Output1", "Output1", "io.audio_output", "Audio Output", [], [new PinDto(0, "Input", "input")], [], [], [], new GraphPositionDto(360, 0))
        ],
        [
            new ConnectionDto(new PinRefDto("Input1", 0, "Output"), new PinRefDto("Gain1", 0, "Input")),
            new ConnectionDto(new PinRefDto("Gain1", 0, "Output"), new PinRefDto("Output1", 0, "Input"))
        ], 1, GraphFreshness.Fresh);

    private static T Deserialize<T>(object? value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions) ?? throw new InvalidOperationException("Invalid command parameters.");
    private static string GetString(object? value, string property) => Deserialize<JsonElement>(value).GetProperty(property).GetString() ?? "";
    private static double GetDouble(object? value, string property) => Deserialize<JsonElement>(value).GetProperty(property).GetDouble();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
