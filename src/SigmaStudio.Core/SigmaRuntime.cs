using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
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

    public async Task<SigmaToolResult<object?>> CatalogDiscoveryAsync(CancellationToken ct)
    {
        return await ExecuteReadOnlyAsync("sigma_catalog_discovery", "catalog.discovery", ct);
    }

    public async Task<SigmaToolResult<object?>> PropertyProbeGetControlValueAsync(PropertyProbeGetControlValueInput input, CancellationToken ct)
    {
        return await ExecuteReadOnlyAsync("sigma_property_probe_get_control_value", "property.probeGetControlValue", input, ct);
    }

    public async Task<SigmaToolResult<object?>> PropertyProbeSetControlValueAsync(PropertyProbeSetControlValueInput input, CancellationToken ct)
    {
        return await ExecuteReadOnlyAsync("sigma_property_probe_set_control_value", "property.probeSetControlValue", input, ct);
    }

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
        return await GraphGetAsync(new GraphGetInput("live"), ct);
    }

    public async Task<SigmaToolResult<object?>> GraphGetAsync(GraphGetInput input, CancellationToken ct)
    {
        if (string.Equals(input.Refresh, "live", StringComparison.OrdinalIgnoreCase))
        {
            var live = await MutateAsync("sigma_graph_refresh_live", "graph.refreshLive", null, null, null, false, ct);
            if (!live.Ok) return live;
        }
        if (string.Equals(input.Refresh, "compile", StringComparison.OrdinalIgnoreCase))
        {
            var link = await MutateAsync("sigma_link", "graph.link", null, null, null, false, ct);
            if (!link.Ok) return link;
            var compile = await MutateAsync("sigma_compile", "graph.compile", null, null, null, false, ct);
            if (!compile.Ok) return compile;
            var live = await MutateAsync("sigma_graph_refresh_live", "graph.refreshLive", null, null, null, false, ct);
            if (!live.Ok) return live;
        }
        return await ReadAsync("sigma_graph_get", ct, snapshot => snapshot.Graph with
        {
            Freshness = string.Equals(input.Refresh, "cached", StringComparison.OrdinalIgnoreCase) && snapshot.State.IsDirty ? GraphFreshness.Stale : snapshot.Graph.Freshness
        });
    }

    public async Task<SigmaToolResult<object?>> GraphTransactionAsync(GraphTransactionInput input, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(input.MutationId) && TryGetMutation(input.MutationId!, out var cached)) return cached;
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(120), ct)) return Failure("sigma_graph_transaction", "BUSY", "Another SigmaStudio operation is in progress.");

        AutomationSnapshot before = await _automation.GetSnapshotAsync(ct);
        var structuralOperations = 0;
        try
        {
            if (before.State.DesignRevision != input.ExpectedDesignRevision)
                return Failure("sigma_graph_transaction", "STALE_REVISION", $"Expected design revision {input.ExpectedDesignRevision}, actual revision is {before.State.DesignRevision}.", snapshot: before);

            var checkpoint = await _automation.ExecuteAsync(new AutomationCommand("project.checkpoint"), ct);
            if (!checkpoint.Ok)
                return Failure("sigma_graph_transaction", checkpoint.ErrorCode ?? "CHECKPOINT_FAILED", checkpoint.ErrorMessage ?? "Transaction checkpoint failed.", checkpoint.Details, before);

            var current = before;
            foreach (var operation in input.Operations)
            {
                var translated = TranslateTransactionOperation(operation, current.Graph);
                if (!translated.Ok) return await RollbackTransactionAsync("sigma_graph_transaction", translated.ErrorCode!, translated.ErrorMessage!, before, structuralOperations, ct);
                var result = await _automation.ExecuteAsync(translated.Command!, ct);
                if (!result.Ok)
                    return await RollbackTransactionAsync("sigma_graph_transaction", result.ErrorCode ?? "TRANSACTION_OPERATION_FAILED", result.ErrorMessage ?? "Transaction operation failed.", before, structuralOperations, ct);
                if (translated.Structural) structuralOperations++;
                current = await _automation.GetSnapshotAsync(ct);
            }

            var refresh = await _automation.ExecuteAsync(new AutomationCommand("graph.refreshLive"), ct);
            if (!refresh.Ok)
                return await RollbackTransactionAsync("sigma_graph_transaction", refresh.ErrorCode ?? "GRAPH_REFRESH_FAILED", refresh.ErrorMessage ?? "Transaction graph refresh failed.", before, structuralOperations, ct);
            current = await _automation.GetSnapshotAsync(ct);

            if (input.Validate)
            {
                var issues = _graphValidator.Validate(current.Graph);
                if (issues.Count > 0)
                    return await RollbackTransactionAsync("sigma_graph_transaction", "GRAPH_VALIDATION_FAILED", "The transaction produced an invalid graph.", before, structuralOperations, ct, new Dictionary<string, object?> { ["issues"] = issues });
            }

            if (input.Deploy)
            {
                foreach (var command in new[] { "graph.link", "graph.compile", "graph.download" })
                {
                    var deploy = await _automation.ExecuteAsync(new AutomationCommand(command), ct);
                    if (!deploy.Ok)
                        return await RollbackTransactionAsync("sigma_graph_transaction", deploy.ErrorCode ?? "DEPLOY_FAILED", deploy.ErrorMessage ?? $"Transaction deploy step '{command}' failed.", before, structuralOperations, ct);
                }
                current = await _automation.GetSnapshotAsync(ct);
            }

            var transaction = new GraphTransactionResultDto(ComputeDiff(before.Graph, current.Graph), current.Graph, input.Validate, input.Deploy);
            var resultEnvelope = Success("sigma_graph_transaction", current, transaction);
            if (!string.IsNullOrWhiteSpace(input.MutationId)) _mutations[input.MutationId!] = new CachedMutation(DateTimeOffset.UtcNow.Add(_mutationRetention), resultEnvelope);
            return resultEnvelope;
        }
        catch (Exception ex)
        {
            return await RollbackTransactionAsync("sigma_graph_transaction", "TRANSACTION_FAILED", ex.Message, before, structuralOperations, ct);
        }
        finally
        {
            _operationLock.Release();
        }
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

    public async Task<SigmaToolResult<object?>> BlockGetAsync(string block, bool refreshControls, CancellationToken ct)
    {
        if (refreshControls)
        {
            var refreshed = await MutateAsync("sigma_block_refresh_controls", "block.getControls", new { block }, block, null, false, ct);
            if (!refreshed.Ok) return refreshed;
        }
        return await ReadAsync("sigma_block_get", ct, snapshot => (object?)FindBlock(snapshot.Graph, block) ?? new { });
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
        await ReadAsync("sigma_block_get_controls", ct, snapshot => (object?)FindBlock(snapshot.Graph, block)?.Controls ?? Array.Empty<ControlDto>());

    public Task<SigmaToolResult<object?>> BlockSetControlAsync(SetControlInput input, CancellationToken ct) =>
        SetControlsVerifiedAsync(new SetControlsInput(input.Block, [new ControlChangeInput(input.Control, input.Value)], input.Mutation), "sigma_block_set_control", ct);

    public Task<SigmaToolResult<object?>> BlockSetControlsAsync(SetControlsInput input, CancellationToken ct) =>
        SetControlsVerifiedAsync(input, "sigma_block_set_controls", ct);

    public Task<SigmaToolResult<object?>> ConnectionAddAsync(ConnectionInput input, CancellationToken ct) =>
        MutateAsync("sigma_connection_add", "connection.add", new ConnectionDto(new PinRefDto(input.SourceBlock, input.SourcePinIndex, input.SourcePinName), new PinRefDto(input.TargetBlock, input.TargetPinIndex, input.TargetPinName)), null, input.Mutation, true, ct);

    public Task<SigmaToolResult<object?>> ConnectionRemoveAsync(ConnectionRemoveInput input, CancellationToken ct) =>
        MutateAsync("sigma_connection_remove", "connection.remove", input.Connection, null, input.Mutation, true, ct);

    public Task<SigmaToolResult<object?>> LinkAsync(CancellationToken ct) => MutateAsync("sigma_link", "graph.link", null, null, null, false, ct);
    public Task<SigmaToolResult<object?>> CompileAsync(CancellationToken ct) => MutateAsync("sigma_compile", "graph.compile", null, null, null, false, ct);
    public Task<SigmaToolResult<object?>> DownloadAsync(CancellationToken ct) => MutateAsync("sigma_download", "graph.download", null, null, null, false, ct);

    private async Task<SigmaToolResult<object?>> SetControlsVerifiedAsync(SetControlsInput input, string operation, CancellationToken ct)
    {
        if (input.Changes is null || input.Changes.Count == 0)
            return Failure(operation, "INVALID_PARAMETERS", "At least one control change is required.");
        if (string.IsNullOrWhiteSpace(input.Mutation.MutationId))
            return Failure(operation, "INVALID_PARAMETERS", "A caller-owned mutationId is required.");
        if (TryGetMutation(input.Mutation.MutationId, out var cached)) return cached;
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(120), ct))
            return Failure(operation, "BUSY", "Another SigmaStudio operation is in progress.");

        try
        {
            var before = await _automation.GetSnapshotAsync(ct);
            if (before.State.DesignRevision != input.Mutation.ExpectedDesignRevision)
                return Failure(operation, "STALE_REVISION", $"Expected design revision {input.Mutation.ExpectedDesignRevision}, actual revision is {before.State.DesignRevision}.", snapshot: before);

            var selectedBlock = FindBlock(before.Graph, input.Block);
            if (selectedBlock is null)
                return Failure(operation, "BLOCK_NOT_FOUND", $"Block '{input.Block}' was not found in the live graph.", snapshot: before);

            // Refresh the exact block through GET_OBJECT_PROPERTY before taking the
            // old-value snapshot. This keeps the write contract block-ID driven while
            // ensuring the restore value is live rather than cached export metadata.
            var refresh = await _automation.ExecuteAsync(new AutomationCommand("block.getControls", new { block = selectedBlock.ObjectName }), ct);
            if (!refresh.Ok)
                return Failure(operation, refresh.ErrorCode ?? "CONTROL_READBACK_UNAVAILABLE", refresh.ErrorMessage ?? "The selected block controls could not be read.", refresh.Details, before);
            var current = await _automation.GetSnapshotAsync(ct);
            selectedBlock = FindBlock(current.Graph, selectedBlock.Id) ?? selectedBlock;

            var requested = new List<(string Name, JsonElement Value)>();
            var original = new List<(string Name, JsonElement Value)>();
            foreach (var change in input.Changes)
            {
                var control = FindControl(selectedBlock, change.Control);
                if (control is null)
                    return Failure(operation, "CONTROL_NOT_FOUND", $"Control '{change.Control}' was not found on block '{selectedBlock.ObjectName}'.", snapshot: current);
                if (!ValidateControlValue(control, change.Value, out var validationError))
                    return Failure(operation, validationError!.Code, validationError.Message, snapshot: current);
                var oldValue = CurrentControlValue(control);
                if (oldValue.ValueKind == JsonValueKind.Undefined)
                    return Failure(operation, "CONTROL_READBACK_UNAVAILABLE", $"The current value for '{control.Name}' could not be read.", snapshot: current);
                requested.Add((control.Name, change.Value.Clone()));
                original.Add((control.Name, oldValue));
            }

            var beforeSequence = current.Capture.LastOrDefault()?.Sequence ?? 0;
            var applied = new List<(string Name, JsonElement Value)>();
            foreach (var change in requested)
            {
                var write = await _automation.ExecuteAsync(new AutomationCommand("block.setControl", new
                {
                    block = selectedBlock.ObjectName,
                    control = change.Name,
                    value = change.Value
                }), ct);
                if (!write.Ok)
                    return await FailedControlVerificationAsync(operation, write.ErrorCode ?? "CONTROL_WRITE_FAILED", write.ErrorMessage ?? "SigmaStudio rejected the control write.", write.Details, selectedBlock.ObjectName, applied, original, current, ct);
                applied.Add(change);
            }

            var readback = await _automation.ExecuteAsync(new AutomationCommand("block.getControls", new { block = selectedBlock.ObjectName }), ct);
            if (!readback.Ok)
                return await FailedControlVerificationAsync(operation, "CONTROL_READBACK_UNAVAILABLE", readback.ErrorMessage ?? "Control read-after-write failed.", readback.Details, selectedBlock.ObjectName, applied, original, current, ct);
            var after = await _automation.GetSnapshotAsync(ct);
            var afterBlock = FindBlock(after.Graph, selectedBlock.Id) ?? FindBlock(after.Graph, selectedBlock.ObjectName);
            var observed = new List<object>();
            var allMatch = true;
            foreach (var change in requested)
            {
                var observedControl = afterBlock is null ? null : FindControl(afterBlock, change.Name);
                var observedValue = observedControl is null ? default : CurrentControlValue(observedControl);
                var match = observedControl is not null && JsonValuesEqual(change.Value, observedValue);
                allMatch &= match;
                observed.Add(new { control = change.Name, requestedValue = change.Value, observedValue, match });
            }

            var captureEntries = after.Capture.Where(entry => entry.Sequence > beforeSequence).ToArray();
            var captureEvidence = new
            {
                available = captureEntries.Length > 0,
                entries = CaptureResponseSerializer.Project(captureEntries, includeRaw: false)
            };
            if (!allMatch || captureEntries.Length == 0)
            {
                return await FailedControlVerificationAsync(
                    operation,
                    !allMatch ? "CONTROL_READBACK_MISMATCH" : "CAPTURE_EVIDENCE_UNAVAILABLE",
                    !allMatch ? "SET_OBJECT_PROPERTY completed but the live readback did not match the requested value." : "SET_OBJECT_PROPERTY and readback succeeded, but no new Capture evidence was observed.",
                    new Dictionary<string, object?> { ["observed"] = observed, ["captureEvidence"] = captureEvidence },
                    selectedBlock.ObjectName,
                    applied,
                    original,
                    after,
                    ct);
            }

            var resultData = new
            {
                block = selectedBlock.ObjectName,
                changes = observed,
                verified = true,
                captureEvidence
            };
            var resultEnvelope = Success(operation, after, resultData);
            _mutations[input.Mutation.MutationId] = new CachedMutation(DateTimeOffset.UtcNow.Add(_mutationRetention), resultEnvelope);
            return resultEnvelope;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<SigmaToolResult<object?>> FailedControlVerificationAsync(
        string operation,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? details,
        string block,
        IReadOnlyList<(string Name, JsonElement Value)> applied,
        IReadOnlyList<(string Name, JsonElement Value)> original,
        AutomationSnapshot snapshot,
        CancellationToken ct)
    {
        var rollbackAttempted = applied.Count > 0;
        var rollbackSucceeded = true;
        for (var index = applied.Count - 1; index >= 0; index--)
        {
            var old = original[index];
            var restore = await _automation.ExecuteAsync(new AutomationCommand("block.setControl", new { block, control = old.Name, value = old.Value }), ct);
            if (!restore.Ok) rollbackSucceeded = false;
        }
        if (rollbackAttempted)
        {
            var refresh = await _automation.ExecuteAsync(new AutomationCommand("block.getControls", new { block }), ct);
            if (!refresh.Ok) rollbackSucceeded = false;
            var restored = await _automation.GetSnapshotAsync(ct);
            var restoredBlock = FindBlock(restored.Graph, block);
            for (var index = 0; index < applied.Count && rollbackSucceeded; index++)
            {
                var actual = restoredBlock is null ? default : FindControl(restoredBlock, original[index].Name);
                rollbackSucceeded = actual is not null && JsonValuesEqual(original[index].Value, CurrentControlValue(actual));
            }
            snapshot = restored;
        }
        var failure = Failure(operation, code, message, details, snapshot);
        return failure with { RollbackAttempted = rollbackAttempted, RollbackSucceeded = rollbackSucceeded };
    }

    private static BlockDto? FindBlock(ProjectGraphDto graph, string block) => graph.Blocks.FirstOrDefault(candidate =>
        string.Equals(candidate.Id, block, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(candidate.ObjectName, block, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(candidate.FullObjectName, block, StringComparison.OrdinalIgnoreCase));

    private static ControlDto? FindControl(BlockDto block, string control) => block.Controls.FirstOrDefault(candidate =>
        string.Equals(candidate.Name, control, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(candidate.ControlId, control, StringComparison.OrdinalIgnoreCase));

    private static JsonElement CurrentControlValue(ControlDto control)
    {
        if (control.TypedValue is JsonElement typed && typed.ValueKind != JsonValueKind.Undefined) return typed.Clone();
        return control.Value is double numeric
            ? ControlValueJson.Number(numeric)
            : default;
    }

    private static bool ValidateControlValue(ControlDto control, JsonElement value, out OperationErrorDto? error)
    {
        if (ControlValueJson.TryGetNumber(value, out var number))
        {
            if (control.Min is not null && number < control.Min || control.Max is not null && number > control.Max)
            {
                error = new OperationErrorDto("CONTROL_VALUE_OUT_OF_RANGE", $"Value for '{control.Name}' is outside its documented range.");
                return false;
            }
        }
        if (control.Enum is { Count: > 0 } && value.ValueKind == JsonValueKind.String && !control.Enum.Contains(value.GetString() ?? "", StringComparer.OrdinalIgnoreCase))
        {
            error = new OperationErrorDto("CONTROL_VALUE_INVALID", $"Value for '{control.Name}' is not one of the documented enum values.");
            return false;
        }
        error = null;
        return true;
    }

    private static bool JsonValuesEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Undefined || right.ValueKind == JsonValueKind.Undefined) return false;
        if (ControlValueJson.TryGetNumber(left, out var leftNumber) && ControlValueJson.TryGetNumber(right, out var rightNumber)) return leftNumber == rightNumber;
        return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
    }

    public async Task<SigmaToolResult<object?>> DeployAsync(CancellationToken ct)
    {
        var link = await LinkAsync(ct);
        if (!link.Ok) return link;
        var compile = await CompileAsync(ct);
        if (!compile.Ok) return compile;
        return await DownloadAsync(ct);
    }

    public async Task<SigmaToolResult<object?>> CaptureGetAsync(CaptureGetInput input, CancellationToken ct)
    {
        return await ReadAsync("sigma_capture_get", ct, snapshot => new
        {
            entries = CaptureResponseSerializer.Project(snapshot.Capture
                .Where(e => input.AfterSequence is null || e.Sequence > input.AfterSequence.Value)
                .Take(Math.Clamp(input.Limit, 1, 1000)).ToArray(), input.IncludeRaw),
            warning = snapshot.CaptureWarning,
            serialization = CaptureResponseSerializer.Metadata(input.IncludeRaw)
        });
    }

    public async Task<SigmaToolResult<object?>> CaptureCursorAsync(CancellationToken ct)
    {
        return await ReadAsync("sigma_capture_cursor", ct, snapshot => new CaptureCursorDto((snapshot.Capture.LastOrDefault()?.Sequence ?? 0) + 1));
    }

    public async Task<SigmaToolResult<object?>> CaptureWaitAsync(long afterSequence, int timeoutMs, bool includeRaw, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Clamp(timeoutMs, 1, _options.CaptureWaitMaxSeconds * 1000));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = await _automation.GetSnapshotAsync(ct);
            var entries = snapshot.Capture.Where(e => e.Sequence > afterSequence).ToArray();
            if (entries.Length > 0)
                return Success("sigma_capture_wait", snapshot, new
                {
                    entries = CaptureResponseSerializer.Project(entries, includeRaw),
                    serialization = CaptureResponseSerializer.Metadata(includeRaw)
                });
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

    private Task<SigmaToolResult<object?>> ExecuteReadOnlyAsync(string operation, string command, CancellationToken ct) =>
        ExecuteReadOnlyAsync(operation, command, null, ct);

    private async Task<SigmaToolResult<object?>> ExecuteReadOnlyAsync(string operation, string command, object? parameters, CancellationToken ct)
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(3), ct)) return Failure(operation, "BUSY", "Another SigmaStudio operation is in progress.");
        try
        {
            var result = await _automation.ExecuteAsync(new AutomationCommand(command, parameters), ct);
            var snapshot = await _automation.GetSnapshotAsync(ct);
            return result.Ok
                ? Success(operation, snapshot, result.Data)
                : Failure(operation, result.ErrorCode ?? "AUTOMATION_FAILED", result.ErrorMessage ?? "Automation command failed.", result.Details, snapshot);
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

    private async Task<SigmaToolResult<object?>> RollbackTransactionAsync(string operation, string code, string message, AutomationSnapshot before, int structuralOperations, CancellationToken ct, IReadOnlyDictionary<string, object?>? details = null)
    {
        var rollbackSucceeded = true;
        for (var index = 0; index < structuralOperations; index++)
        {
            var undo = await _automation.ExecuteAsync(new AutomationCommand("project.undo"), ct);
            if (!undo.Ok) { rollbackSucceeded = false; break; }
        }
        var refresh = await _automation.ExecuteAsync(new AutomationCommand("graph.refreshLive"), ct);
        if (!refresh.Ok) rollbackSucceeded = false;
        var after = await _automation.GetSnapshotAsync(ct);
        if (rollbackSucceeded && GraphIdentity.Fingerprint(before.Graph) != GraphIdentity.Fingerprint(after.Graph)) rollbackSucceeded = false;
        var errorDetails = new Dictionary<string, object?>(details ?? new Dictionary<string, object?>())
        {
            ["originalFingerprint"] = GraphIdentity.Fingerprint(before.Graph),
            ["observedFingerprint"] = GraphIdentity.Fingerprint(after.Graph)
        };
        return new SigmaToolResult<object?>(
            false,
            Guid.NewGuid().ToString("N"),
            after.State.DesignRevision,
            after.State.RuntimeRevision,
            after.State.SigmaStudioState,
            after.State.ReadyForMeasurement,
            rollbackSucceeded ? [] : ["ROLLBACK_FAILED"],
            null,
            new OperationErrorDto(rollbackSucceeded ? code : "ROLLBACK_FAILED", rollbackSucceeded ? message : $"{message} Rollback did not restore the original graph.", errorDetails),
            true,
            rollbackSucceeded);
    }

    private TransactionTranslation TranslateTransactionOperation(JsonElement operation, ProjectGraphDto graph)
    {
        var type = GetRequiredString(operation, "type").ToLowerInvariant();
        switch (type)
        {
            case "addblock":
            {
                var catalogId = GetRequiredString(operation, "catalogId");
                var catalog = Catalog.FirstOrDefault(block => string.Equals(block.Id, catalogId, StringComparison.OrdinalIgnoreCase));
                if (catalog is null) return TransactionTranslation.Failure("BLOCK_TYPE_UNKNOWN", $"Catalog block '{catalogId}' was not found.");
                var objectName = GetRequiredString(operation, "objectName");
                var x = operation.TryGetProperty("x", out var xElement) && xElement.ValueKind == JsonValueKind.Number ? xElement.GetDouble() : (double?)null;
                var y = operation.TryGetProperty("y", out var yElement) && yElement.ValueKind == JsonValueKind.Number ? yElement.GetDouble() : (double?)null;
                return TransactionTranslation.Success(new AutomationCommand("block.add", new { catalog, objectName, x, y }), true);
            }
            case "removeblock":
            {
                var block = ResolveBlock(graph, GetRequiredString(operation, "blockId"));
                return block is null ? TransactionTranslation.Failure("BLOCK_NOT_FOUND", "Transaction block was not found.") : TransactionTranslation.Success(new AutomationCommand("block.remove", new { block = block.ObjectName }), true);
            }
            case "renameblock":
            {
                var block = ResolveBlock(graph, GetRequiredString(operation, "blockId"));
                var newName = GetRequiredString(operation, "newName");
                return block is null ? TransactionTranslation.Failure("BLOCK_NOT_FOUND", "Transaction block was not found.") : TransactionTranslation.Success(new AutomationCommand("block.rename", new { block = block.ObjectName, newName }), true);
            }
            case "removeconnection":
            {
                var id = GetRequiredString(operation, "connectionId");
                var connection = graph.Connections.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
                return connection is null ? TransactionTranslation.Failure("CONNECTION_NOT_FOUND", $"Connection '{id}' was not found.") : TransactionTranslation.Success(new AutomationCommand("connection.remove", connection), true);
            }
            case "addconnection":
            {
                var source = ResolveEndpoint(operation.GetProperty("source"), graph);
                var target = ResolveEndpoint(operation.GetProperty("target"), graph);
                if (source is null || target is null) return TransactionTranslation.Failure("CONNECTION_INVALID", "Transaction endpoint block or pin was not found.");
                return TransactionTranslation.Success(new AutomationCommand("connection.add", new ConnectionDto(source, target)), true);
            }
            case "setcontrol":
            {
                var block = ResolveBlock(graph, GetRequiredString(operation, "blockId"));
                var control = block?.Controls.FirstOrDefault(candidate => string.Equals(candidate.ControlId, GetOptionalString(operation, "controlId"), StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.Name, GetOptionalString(operation, "control"), StringComparison.OrdinalIgnoreCase));
                if (block is null || control is null) return TransactionTranslation.Failure("CONTROL_NOT_FOUND", "Transaction control was not found.");
                if (!operation.TryGetProperty("value", out var value)) return TransactionTranslation.Failure("INVALID_PARAMETERS", "Transaction control value is required.");
                return TransactionTranslation.Success(new AutomationCommand("block.setControl", new { block = block.ObjectName, control = control.Name, value = value.Clone() }), false);
            }
            default:
                return TransactionTranslation.Failure("TRANSACTION_OPERATION_UNSUPPORTED", $"Unsupported transaction operation '{type}'.");
        }
    }

    private static PinRefDto? ResolveEndpoint(JsonElement endpoint, ProjectGraphDto graph)
    {
        var block = ResolveBlock(graph, GetOptionalString(endpoint, "blockId") ?? GetOptionalString(endpoint, "block") ?? "");
        if (block is null || !endpoint.TryGetProperty("pinIndex", out var indexElement) || !indexElement.TryGetInt32(out var index)) return null;
        var name = GetOptionalString(endpoint, "pinName") ?? (block.Inputs.Concat(block.Outputs).FirstOrDefault(pin => pin.Index == index)?.Name ?? "");
        return new PinRefDto(block.ObjectName, index, name, block.Id);
    }

    private static BlockDto? ResolveBlock(ProjectGraphDto graph, string block) => graph.Blocks.FirstOrDefault(candidate => string.Equals(candidate.Id, block, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.ObjectName, block, StringComparison.OrdinalIgnoreCase));
    private static string GetRequiredString(JsonElement element, string name) => GetOptionalString(element, name) ?? throw new InvalidOperationException($"Transaction property '{name}' is required.");
    private static string? GetOptionalString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static GraphDiffDto ComputeDiff(ProjectGraphDto before, ProjectGraphDto after)
    {
        var beforeBlocks = before.Blocks.Select(block => block.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterBlocks = after.Blocks.Select(block => block.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var beforeConnections = before.Connections.Select(connection => connection.Id ?? $"{connection.Source.Block}:{connection.Source.PinIndex}->{connection.Target.Block}:{connection.Target.PinIndex}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterConnections = after.Connections.Select(connection => connection.Id ?? $"{connection.Source.Block}:{connection.Source.PinIndex}->{connection.Target.Block}:{connection.Target.PinIndex}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new GraphDiffDto(before.DesignRevision, after.DesignRevision,
            afterBlocks.Except(beforeBlocks, StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            beforeBlocks.Except(afterBlocks, StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            afterConnections.Except(beforeConnections, StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            beforeConnections.Except(afterConnections, StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToArray(), []);
    }

    private sealed record TransactionTranslation(bool Ok, AutomationCommand? Command, bool Structural, string? ErrorCode, string? ErrorMessage)
    {
        public static TransactionTranslation Success(AutomationCommand command, bool structural) => new(true, command, structural, null, null);
        public static TransactionTranslation Failure(string code, string message) => new(false, null, false, code, message);
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
