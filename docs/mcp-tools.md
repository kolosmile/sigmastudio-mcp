# MCP tools

A toolok `sigma_` prefixet használnak. A host a következő csoportokat adja:

- állapot és diagnosztika: `sigma_status`, `sigma_ready_for_measurement`, `sigma_diagnostics`;
- projekt: create/open/save/save-as/close/checkpoint/undo/redo/export;
- graph: get/validate;
- katalógus: block search/docs;
- blokk és kapcsolat: get/add/remove/rename/control/connection;
- build: link/compile/download/deploy;
- capture: get/cursor/wait.

Minden eredmény `ok`, `operationId`, revision, SigmaStudio state, `readyForMeasurement`, warning és `data` mezőket tartalmaz. Design mutációk `expectedDesignRevision` + `mutationId` contractot használnak.
