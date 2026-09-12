# Agent workflow

The MCP is an introspection and mutation control plane. An agent should keep the following order:

1. Call `sigma_status` and `sigma_graph_get({"refresh":"live"})`.
2. Select relevant blocks by stable `block.id`; do not infer topology from the UI LinkWnd.
3. Call `sigma_block_get` with the selected block ID. Use `refreshControls=true` when a fresh control observation is required.
4. Call `sigma_block_docs` for the returned `catalogId` before choosing an unfamiliar block type.
5. Apply one explicit, revision-guarded mutation, or use `sigma_graph_transaction` for a structural batch.
6. Read the graph or block again and check IDs, values, warnings, and `graphFingerprint`.
7. Run `sigma_graph_validate`; deploy only after the graph is structurally valid.
8. Use `sigma_capture_get` only for communication evidence. Capture is not the source of structural graph truth.

## Freshness and evidence

The export is the structural source of truth. A live graph is produced from `EXPORT_SYSTEM_FILES`, `*_NetList.xml`, and the schematic XML. Current control values are marked with their source; the Bridge does not claim a property readback when `GET_OBJECT_PROPERTY` did not return a value. UI status comes from the SigmaStudio status bar. Capture entries come from the accessibility tree when rows are exposed, or from the verified selected-range clipboard route for the owner-drawn 4.7 grid; Capture remains communication evidence, not structural graph truth.

If a response contains `CONTROL_READBACK_UNAVAILABLE`, `GRAPH_EXTRACTION_FAILED`, `ROLLBACK_FAILED`, or `projectStateUncertain`, stop and report that evidence instead of treating the operation as successful.

## Mutation safety

Every design mutation uses `expectedDesignRevision` and a caller-owned `mutationId`. Structural transactions checkpoint with an export snapshot, execute only supported operations, refresh the live graph, validate, and use the verified SigmaStudio undo path on failure. The checkpoint is not a direct `.dspproj` edit.
