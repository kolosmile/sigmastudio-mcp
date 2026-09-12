using System.ComponentModel;
using ModelContextProtocol.Server;
using SigmaStudio.Contracts;
using SigmaStudio.Core;

namespace SigmaStudio.Mcp.Host;

[McpServerToolType]
public sealed class SigmaTools
{
    private readonly SigmaRuntime _runtime;

    public SigmaTools(SigmaRuntime runtime) => _runtime = runtime;

    [McpServerTool(Name = "sigma_status", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Read the current SigmaStudio application, project, revision and deployment state.")]
    public Task<SigmaToolResult<object?>> Status(CancellationToken cancellationToken) => _runtime.StatusAsync(cancellationToken);

    [McpServerTool(Name = "sigma_ready_for_measurement", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Return whether the DSP is safely ready for an external measurement iteration.")]
    public Task<SigmaToolResult<object?>> ReadyForMeasurement(CancellationToken cancellationToken) => _runtime.ReadyForMeasurementAsync(cancellationToken);

    [McpServerTool(Name = "sigma_diagnostics", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Run local SigmaStudio MCP diagnostics without changing the project.")]
    public Task<SigmaToolResult<object?>> Diagnostics(CancellationToken cancellationToken) => _runtime.DiagnosticsAsync(cancellationToken);

    [McpServerTool(Name = "sigma_project_create", UseStructuredContent = true), Description("Create a usable ADAU1701 project in the configured project root.")]
    public Task<SigmaToolResult<object?>> ProjectCreate(ProjectCreateInput input, CancellationToken cancellationToken) => _runtime.ProjectCreateAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_project_open", UseStructuredContent = true), Description("Open an existing SigmaStudio project through the configured backend.")]
    public Task<SigmaToolResult<object?>> ProjectOpen(ProjectOpenInput input, CancellationToken cancellationToken) => _runtime.ProjectOpenAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_project_save", Idempotent = true, UseStructuredContent = true), Description("Save the currently open project.")]
    public Task<SigmaToolResult<object?>> ProjectSave(CancellationToken cancellationToken) => _runtime.ProjectSaveAsync(cancellationToken);

    [McpServerTool(Name = "sigma_project_save_as", UseStructuredContent = true), Description("Save the currently open project under another path.")]
    public Task<SigmaToolResult<object?>> ProjectSaveAs(ProjectSaveAsInput input, CancellationToken cancellationToken) => _runtime.ProjectSaveAsAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_project_close", UseStructuredContent = true), Description("Close the project; unsaved changes are rejected.")]
    public Task<SigmaToolResult<object?>> ProjectClose(CancellationToken cancellationToken) => _runtime.ProjectCloseAsync(cancellationToken);

    [McpServerTool(Name = "sigma_project_checkpoint", ReadOnly = false, Idempotent = false, UseStructuredContent = true), Description("Create a recoverable project checkpoint under .sigmastudio-mcp/backups.")]
    public Task<SigmaToolResult<object?>> ProjectCheckpoint(CancellationToken cancellationToken) => _runtime.ProjectCheckpointAsync(cancellationToken);

    [McpServerTool(Name = "sigma_project_undo", UseStructuredContent = true), Description("Undo the previous design mutation using an expected design revision and mutation id.")]
    public Task<SigmaToolResult<object?>> ProjectUndo(MutationInput mutation, CancellationToken cancellationToken) => _runtime.ProjectUndoAsync(mutation, cancellationToken);

    [McpServerTool(Name = "sigma_project_redo", UseStructuredContent = true), Description("Redo the previous design mutation using an expected design revision and mutation id.")]
    public Task<SigmaToolResult<object?>> ProjectRedo(MutationInput mutation, CancellationToken cancellationToken) => _runtime.ProjectRedoAsync(mutation, cancellationToken);

    [McpServerTool(Name = "sigma_project_export", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Return the normalized export/graph representation of the current project.")]
    public Task<SigmaToolResult<object?>> ProjectExport(CancellationToken cancellationToken) => _runtime.ProjectExportAsync(cancellationToken);

    [McpServerTool(Name = "sigma_graph_get", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Read the semantic DSP graph; refresh can be cached or compile.")]
    public Task<SigmaToolResult<object?>> GraphGet(GraphGetInput input, CancellationToken cancellationToken) => _runtime.GraphGetAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_graph_transaction", UseStructuredContent = true), Description("Execute a guarded multi-operation graph transaction with checkpoint, validation and rollback.")]
    public Task<SigmaToolResult<object?>> GraphTransaction(GraphTransactionInput input, CancellationToken cancellationToken) => _runtime.GraphTransactionAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_graph_validate", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Validate graph block, pin and connection references.")]
    public Task<SigmaToolResult<object?>> GraphValidate(CancellationToken cancellationToken) => _runtime.GraphValidateAsync(cancellationToken);

    [McpServerTool(Name = "sigma_block_search", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Search the offline ADAU1701 block knowledge base.")]
    public Task<SigmaToolResult<object?>> BlockSearch(string query = "", int limit = 20, CancellationToken cancellationToken = default) => _runtime.BlockSearchAsync(query, limit, cancellationToken);

    [McpServerTool(Name = "sigma_block_docs", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Return normalized, provenance-bearing documentation for a catalog block.")]
    public Task<SigmaToolResult<object?>> BlockDocs(string catalogId, CancellationToken cancellationToken) => _runtime.BlockDocsAsync(catalogId, cancellationToken);

    [McpServerTool(Name = "sigma_catalog_discovery", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Report whether the installed SigmaStudio runtime exposes a verified Toolbox catalog and which automation methods were observed.")]
    public Task<SigmaToolResult<object?>> CatalogDiscovery(CancellationToken cancellationToken) => _runtime.CatalogDiscoveryAsync(cancellationToken);

    [McpServerTool(Name = "sigma_property_probe_get_control_value", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Read-only developer probe for the verified SigmaStudio getControlValue property contract. Provide the exact exported object name, algorithm index, repeat index and control parameter name.")]
    public Task<SigmaToolResult<object?>> PropertyProbeGetControlValue(PropertyProbeGetControlValueInput input, CancellationToken cancellationToken) => _runtime.PropertyProbeGetControlValueAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_block_get", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Read one block by stable blockId (or legacy objectName), including current control metadata; refreshControls forces a fresh live export.")]
    public Task<SigmaToolResult<object?>> BlockGet(string block, bool refreshControls = false, CancellationToken cancellationToken = default) => _runtime.BlockGetAsync(block, refreshControls, cancellationToken);

    [McpServerTool(Name = "sigma_block_add", UseStructuredContent = true), Description("Add an ADAU1701 block using catalog metadata and a guarded mutation contract.")]
    public Task<SigmaToolResult<object?>> BlockAdd(BlockAddInput input, CancellationToken cancellationToken) => _runtime.BlockAddAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_block_remove", UseStructuredContent = true), Description("Remove a block and its connected edges using a guarded mutation contract.")]
    public Task<SigmaToolResult<object?>> BlockRemove(BlockRemoveInput input, CancellationToken cancellationToken) => _runtime.BlockRemoveAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_block_rename", UseStructuredContent = true), Description("Rename a block through the backend rather than editing a dspproj file.")]
    public Task<SigmaToolResult<object?>> BlockRename(BlockRenameInput input, CancellationToken cancellationToken) => _runtime.BlockRenameAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_block_get_controls", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Return controls for a graph block.")]
    public Task<SigmaToolResult<object?>> BlockGetControls(string block, CancellationToken cancellationToken) => _runtime.BlockGetControlsAsync(block, cancellationToken);

    [McpServerTool(Name = "sigma_block_set_control", UseStructuredContent = true), Description("Set one typed SigmaStudio control only through a verified property contract. Call sigma_graph_get first, select a stable blockId, call sigma_block_get with refreshControls=true, and never invent a control name.")]
    public Task<SigmaToolResult<object?>> BlockSetControl(SetControlInput input, CancellationToken cancellationToken) => _runtime.BlockSetControlAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_block_set_controls", UseStructuredContent = true), Description("Apply typed control changes under one operation lock; the backend must have a verified property contract and read-after-write policy.")]
    public Task<SigmaToolResult<object?>> BlockSetControls(SetControlsInput input, CancellationToken cancellationToken) => _runtime.BlockSetControlsAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_connection_add", UseStructuredContent = true), Description("Create a semantic graph connection using explicit source and target pins.")]
    public Task<SigmaToolResult<object?>> ConnectionAdd(ConnectionInput input, CancellationToken cancellationToken) => _runtime.ConnectionAddAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_connection_remove", UseStructuredContent = true), Description("Remove a semantic graph connection using explicit source and target pins.")]
    public Task<SigmaToolResult<object?>> ConnectionRemove(ConnectionRemoveInput input, CancellationToken cancellationToken) => _runtime.ConnectionRemoveAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_link", UseStructuredContent = true), Description("Run SigmaStudio link without downloading to hardware.")]
    public Task<SigmaToolResult<object?>> Link(CancellationToken cancellationToken) => _runtime.LinkAsync(cancellationToken);

    [McpServerTool(Name = "sigma_compile", UseStructuredContent = true), Description("Compile the current design without downloading to hardware.")]
    public Task<SigmaToolResult<object?>> Compile(CancellationToken cancellationToken) => _runtime.CompileAsync(cancellationToken);

    [McpServerTool(Name = "sigma_download", UseStructuredContent = true), Description("Download the compiled design to the connected ADAU1701 backend.")]
    public Task<SigmaToolResult<object?>> Download(CancellationToken cancellationToken) => _runtime.DownloadAsync(cancellationToken);

    [McpServerTool(Name = "sigma_deploy", UseStructuredContent = true), Description("Run link, compile and download as one serialized deployment operation.")]
    public Task<SigmaToolResult<object?>> Deploy(CancellationToken cancellationToken) => _runtime.DeployAsync(cancellationToken);

    [McpServerTool(Name = "sigma_capture_get", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Read recent Capture Window entries after an optional sequence cursor.")]
    public Task<SigmaToolResult<object?>> CaptureGet(CaptureGetInput input, CancellationToken cancellationToken) => _runtime.CaptureGetAsync(input, cancellationToken);

    [McpServerTool(Name = "sigma_capture_cursor", ReadOnly = true, Idempotent = true, UseStructuredContent = true), Description("Return the next Capture Window sequence cursor.")]
    public Task<SigmaToolResult<object?>> CaptureCursor(CancellationToken cancellationToken) => _runtime.CaptureCursorAsync(cancellationToken);

    [McpServerTool(Name = "sigma_capture_wait", ReadOnly = true, UseStructuredContent = true), Description("Wait for a new Capture Window entry up to the configured timeout.")]
    public Task<SigmaToolResult<object?>> CaptureWait(long afterSequence, int timeoutMs = 30000, CancellationToken cancellationToken = default) => _runtime.CaptureWaitAsync(afterSequence, timeoutMs, cancellationToken);
}
