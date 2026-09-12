using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SigmaStudio.Contracts;
using SigmaStudio.Graph;

namespace SigmaStudio.Core;

public sealed class SigmaRuntime
{
    private readonly ISigmaStudioAutomation _automation;
    private readonly ProjectPathPolicy _paths;
    private readonly GraphValidator _graphValidator;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CachedMutation> _mutations = new(StringComparer.Ordinal);
    private readonly TimeSpan _mutationRetention = TimeSpan.FromMinutes(10);
    private readonly Func<IReadOnlyList<CatalogBlockDto>> _catalog;
    private readonly SigmaStudioOptions _options;

    public SigmaRuntime(
        ISigmaStudioAutomation automation,
        ProjectPathPolicy paths,
        GraphValidator graphValidator,
        Func<IReadOnlyList<CatalogBlockDto>> catalog,
        SigmaStudioOptions options)
    {
        _automation = automation;
        _paths = paths;
        _graphValidator = graphValidator;
        _catalog = catalog;
        _options = options;
    }

    public string BackendName => _automation.BackendName;
    public IReadOnlyList<CatalogBlockDto> Catalog => _catalog();

    public Task<SigmaToolResult<object?>> StatusAsync(CancellationToken ct) => ReadAsync("sigma_status", ct, snapshot => snapshot.State);

    public async Task<SigmaToolResult<object?>> ReadyForMeasurementAsync(CancellationToken ct)
    {
        var result = await ReadAsync("sigma_ready_for_measurement", ct, snapshot => new
        {
            readyForMeasurement = snapshot.State.ReadyForMeasurement,
            state = snapshot.State.SigmaStudioState,
            designRevision = snapshot.State.DesignRevision,
            deployedDesignRevision = snapshot.State.DeployedDesignRevision
        });
        return result;
    }

    public async Task<SigmaToolResult<object?>> DiagnosticsAsync(CancellationToken ct)
    {
        var snapshot = await _automation.GetSnapshotAsync(ct);
        var serverPath = _options.SigmaStudioServerPath;
        var diagnostics = new DiagnosticsDto(
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            BackendName,
            !string.IsNullOrWhiteSpace(serverPath) && File.Exists(serverPath),
            Process.GetProcessesByName("SStudio").Length > 0,
            snapshot.Connected,
            _options.SigmaStudioInstallPath,
            serverPath,
            OperatingSystem.IsWindows() ? "not-probed" : "unavailable-non-windows",
            "1.0.0",
            BackendName.Equals("InMemory", StringComparison.OrdinalIgnoreCase)
                ? ["InMemory backend is active; SigmaStudio 4.7 hardware integration is not being exercised."]
                : []);
        return Success("sigma_diagnostics", snapshot, diagnostics);
    }

    public Task<SigmaToolResult<object?>> ProjectCreateAsync(ProjectCreateInput input, CancellationToken ct) =>
        MutateAsync("sigma_project_create", "project.create", input with { Path = _paths.Validate(input.Path) }, null, null, true, ct);

    public Task<SigmaToolResult<object?>> ProjectOpenAsync(ProjectOpenInput input, CancellationToken ct) =>
        MutateAsync("sigma_project_open", "project.open", input with { Path = _paths.Validate(input.Path) }, null, null, true, ct);

    public Task<SigmaToolResult<object?>> ProjectSaveAsync(CancellationToken ct) => MutateAsync("sigma_project_save", "project.save", null, null, null, false, ct);

    public Task<SigmaToolResult<object?>> ProjectSaveAsAsync(ProjectSaveAsInput input, CancellationToken ct) =>
        MutateAsync("sigma_project_save_as", "project.saveAs", input with { Path = _paths.Validate(input.Path) }, null, null, false, ct);

    public Task<SigmaToolResult<object?>> ProjectCloseAsync(CancellationToken ct) => MutateAsync("sigma_project_close", "project.close", null, null, null, false, ct);
    public Task<SigmaToolResult<object?>> ProjectCheckpointAsync(CancellationToken ct) => MutateAsync("sigma_project_checkpoint", "project.checkpoint", null, null, null, false, ct);
    public Task<SigmaToolResult<object?>> ProjectUndoAsync(MutationInput mutation, CancellationToken ct) => MutateAsync("sigma_project_undo", "project.undo", null, null, mutation, true, ct);
    public Task<SigmaToolResult<object?>> ProjectRedoAsync(MutationInput mutation, CancellationToken ct) => MutateAsync("sigma_project_redo", "project.redo", null, null, mutation, true, ct);

    public async Task<SigmaToolResult<object?>> ProjectExportAsync(CancellationToken ct)
    {
        return await ReadAsync("sigma_project_export", ct, snapshot => snapshot.Graph);
    }

    public async Task<SigmaToolResult<object?>> GraphGetAsync(GraphGetInput input, CancellationToken ct)
    {
        if (string.Equals(input.Refresh, "compile", StringComparison.OrdinalIgnoreCase))
        {
            await MutateAsync("sigma_link", "graph.link", null, null, null, false, ct);
            await MutateAsync("sigma_compile", "graph.compile", null, null, null, false, ct);
        }
        return await ReadAsync("sigma_graph_get", ct, snapshot => snapshot.Graph with
        {
            Freshness = string.Equals(input.Refresh, "cached", StringComparison.OrdinalIgnoreCase) && snapshot.State.IsDirty ? GraphFreshness.Stale : snapshot.Graph.Freshness
        });
    }

    public async Task<SigmaToolResult<object?>> GraphValidateAsync(CancellationToken ct)
    {
        return await ReadAsync("sigma_graph_validate", ct, snapshot => _graphValidator.Validate(snapshot.Graph));
    }

    public async Task<SigmaToolResult<object?>> BlockSearchAsync(string query, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        return await ReadAsync("sigma_block_search", ct, _ => Catalog.Where(b =>
            string.IsNullOrWhiteSpace(query) ||
            b.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            b.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            b.ToolboxPath.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit).ToArray());
    }

    public async Task<SigmaToolResult<object?>> BlockDocsAsync(string catalogId, CancellationToken ct)
    {
        return await ReadAsync("sigma_block_docs", ct, _ => (object?)Catalog.FirstOrDefault(b => string.Equals(b.Id, catalogId, StringComparison.OrdinalIgnoreCase)) ?? new { });
    }

    public async Task<SigmaToolResult<object?>> BlockGetAsync(string block, CancellationToken ct)
    {
        return await ReadAsync("sigma_block_get", ct, snapshot => (object?)snapshot.Graph.Blocks.FirstOrDefault(b => string.Equals(b.ObjectName, block, StringComparison.OrdinalIgnoreCase)) ?? new { });
    }

    public Task<SigmaToolResult<object?>> BlockAddAsync(BlockAddInput input, CancellationToken ct)
    {
        var catalog = Catalog.FirstOrDefault(b => string.Equals(b.Id, input.CatalogId, StringComparison.OrdinalIgnoreCase));
        if (catalog is null) return Task.FromResult(Failure("sigma_block_add", "BLOCK_TYPE_UNKNOWN", $"Catalog block '{input.CatalogId}' was not found."));
        return MutateAsync("sigma_block_add", "block.add", new { catalog, objectName = input.ObjectName, x = input.X, y = input.Y }, null, input.Mutation, true, ct);
    }

    public Task<SigmaToolResult<object?>> BlockRemoveAsync(BlockRemoveInput input, CancellationToken ct) =>
        MutateAsync("sigma_block_remove", "block.remove", new { block = input.Block }, input.Block, input.Mutation, true, ct);

    public Task<SigmaToolResult<object?>> BlockRenameAsync(BlockRenameInput input, CancellationToken ct) =>
        MutateAsync("sigma_block_rename", "block.rename", new { block = input.Block, newName = input.NewName }, input.Block, input.Mutation, true, ct);

    public async Task<SigmaToolResult<object?>> BlockGetControlsAsync(string block, CancellationToken ct) =>
        await ReadAsync("sigma_block_get_controls", ct, snapshot => (object?)snapshot.Graph.Blocks.FirstOrDefault(b => string.Equals(b.ObjectName, block, StringComparison.OrdinalIgnoreCase))?.Controls ?? Array.Empty<ControlDto>());

    public Task<SigmaToolResult<object?>> BlockSetControlAsync(SetControlInput input, CancellationToken ct) =>
        MutateAsync("sigma_block_set_control", "block.setControl", new { block = input.Block, control = input.Control, value = input.Value }, input.Block, input.Mutation, false, ct);

    public Task<SigmaToolResult<object?>> BlockSetControlsAsync(SetControlsInput input, CancellationToken ct) =>
        MutateAsync("sigma_block_set_controls", "block.setControls", new { block = input.Block, changes = input.Changes }, input.Block, input.Mutation, false, ct);

    public Task<SigmaToolResult<object?>> ConnectionAddAsync(ConnectionInput input, CancellationToken ct) =>
        MutateAsync("sigma_connection_add", "connection.add", new ConnectionDto(new PinRefDto(input.SourceBlock, input.SourcePinIndex, input.SourcePinName), new PinRefDto(input.TargetBlock, input.TargetPinIndex, input.TargetPinName)), null, input.Mutation, true, ct);

    public Task<SigmaToolResult<object?>> ConnectionRemoveAsync(ConnectionRemoveInput input, CancellationToken ct) =>
        MutateAsync("sigma_connection_remove", "connection.remove", input.Connection, null, input.Mutation, true, ct);

    public Task<SigmaToolResult<object?>> LinkAsync(CancellationToken ct) => MutateAsync("sigma_link", "graph.link", null, null, null, false, ct);
    public Task<SigmaToolResult<object?>> CompileAsync(CancellationToken ct) => MutateAsync("sigma_compile", "graph.compile", null, null, null, false, ct);
    public Task<SigmaToolResult<object?>> DownloadAsync(CancellationToken ct) => MutateAsync("sigma_download", "graph.download", null, null, null, false, ct);

    public async Task<SigmaToolResult<object?>> DeployAsync(CancellationToken ct)
    {
        await LinkAsync(ct);
        await CompileAsync(ct);
        return await DownloadAsync(ct);
    }

    public async Task<SigmaToolResult<object?>> CaptureGetAsync(CaptureGetInput input, CancellationToken ct)
    {
        return await ReadAsync("sigma_capture_get", ct, snapshot => snapshot.Capture
            .Where(e => input.AfterSequence is null || e.Sequence > input.AfterSequence.Value)
            .Take(Math.Clamp(input.Limit, 1, 1000)).ToArray());
    }

    public async Task<SigmaToolResult<object?>> CaptureCursorAsync(CancellationToken ct)
    {
        return await ReadAsync("sigma_capture_cursor", ct, snapshot => new CaptureCursorDto((snapshot.Capture.LastOrDefault()?.Sequence ?? 0) + 1));
    }

    public async Task<SigmaToolResult<object?>> CaptureWaitAsync(long afterSequence, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Clamp(timeoutMs, 1, _options.CaptureWaitMaxSeconds * 1000));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = await _automation.GetSnapshotAsync(ct);
            var entries = snapshot.Capture.Where(e => e.Sequence > afterSequence).ToArray();
            if (entries.Length > 0) return Success("sigma_capture_wait", snapshot, entries);
            await Task.Delay(100, ct);
        }
        return Failure("sigma_capture_wait", "TIMEOUT", "No new Capture Window entry arrived before the timeout.");
    }

    public bool RawParameterAccessEnabled => _options.EnableRawParameterAccess;
    public bool RawRegisterAccessEnabled => _options.EnableRawRegisterAccess;

    private async Task<SigmaToolResult<object?>> MutateAsync(string operation, string command, object? parameters, string? block, MutationInput? mutation, bool designMutation, CancellationToken ct, bool allowCache = true)
    {
        if (mutation is not null && allowCache && TryGetMutation(mutation.MutationId, out var cached)) return cached;
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(120), ct)) return Failure(operation, "BUSY", "Another SigmaStudio operation is in progress.");
        try
        {
            var before = await _automation.GetSnapshotAsync(ct);
            if (mutation is not null && before.State.DesignRevision != mutation.ExpectedDesignRevision)
                return Failure(operation, "STALE_REVISION", $"Expected design revision {mutation.ExpectedDesignRevision}, actual revision is {before.State.DesignRevision}.");
            var stopwatch = Stopwatch.StartNew();
            var result = await _automation.ExecuteAsync(new AutomationCommand(command, parameters), ct);
            var after = await _automation.GetSnapshotAsync(ct);
            var envelope = result.Ok
                ? Success(operation, after, result.Data)
                : Failure(operation, result.ErrorCode ?? "INTERNAL_ERROR", result.ErrorMessage ?? "SigmaStudio operation failed.", result.Details, after);
            stopwatch.Stop();
            if (mutation is not null && result.Ok) _mutations[mutation.MutationId] = new CachedMutation(DateTimeOffset.UtcNow.Add(_mutationRetention), envelope);
            return envelope;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<SigmaToolResult<object?>> ReadAsync(string operation, CancellationToken ct, Func<AutomationSnapshot, object?> projector)
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(3), ct)) return Failure(operation, "BUSY", "Another SigmaStudio operation is in progress.");
        try
        {
            var snapshot = await _automation.GetSnapshotAsync(ct);
            return Success(operation, snapshot, projector(snapshot));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private static SigmaToolResult<object?> Success(string operation, AutomationSnapshot snapshot, object? data) => new(true, Guid.NewGuid().ToString("N"), snapshot.State.DesignRevision, snapshot.State.RuntimeRevision, snapshot.State.SigmaStudioState, snapshot.State.ReadyForMeasurement, [], data);

    private static SigmaToolResult<object?> Failure(string operation, string code, string message, IReadOnlyDictionary<string, object?>? details = null, AutomationSnapshot? snapshot = null)
    {
        var state = snapshot?.State;
        return new(false, Guid.NewGuid().ToString("N"), state?.DesignRevision ?? 0, state?.RuntimeRevision ?? 0, state?.SigmaStudioState ?? SigmaStudioState.Unknown, state?.ReadyForMeasurement ?? false, [], null, new OperationErrorDto(code, message, details));
    }

    private bool TryGetMutation(string id, out SigmaToolResult<object?> result)
    {
        result = default!;
        if (!_mutations.TryGetValue(id, out var cached)) return false;
        if (cached.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _mutations.TryRemove(id, out _);
            return false;
        }
        result = cached.Result;
        return true;
    }

    private sealed record CachedMutation(DateTimeOffset ExpiresAt, SigmaToolResult<object?> Result);
}
