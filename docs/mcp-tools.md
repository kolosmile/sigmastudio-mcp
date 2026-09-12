# MCP tools

A toolok `sigma_` prefixet használnak. A host a következő csoportokat adja:

- állapot és diagnosztika: `sigma_status`, `sigma_ready_for_measurement`, `sigma_diagnostics`;
- projekt: create/open/save/save-as/close/checkpoint/undo/redo/export;
- graph: get/validate/transaction;
- katalógus: block search/docs;
- blokk és kapcsolat: get/add/remove/rename/control/connection; `sigma_block_get` stable block ID-t és opcionális `refreshControls` flaget fogad; `sigma_property_probe_get_control_value` read-only fejlesztői probe a helyi `getControlValue` szerződéshez; `sigma_property_probe_set_control_value` kizárólag explicit disposable HIL projektben használható contract-probe;
- catalog: offline docs és `sigma_catalog_discovery` (a telepített Toolbox enumerációjának bizonyíték-státusza);
- build: link/compile/download/deploy;
- capture: get/cursor/wait.

Minden eredmény `ok`, `operationId`, revision, SigmaStudio state, `readyForMeasurement`, warning és `data` mezőket tartalmaz. A Bridge snapshotban külön `commandState`, nyers `observedUiState` és normalizált `normalizedState` jelenik meg; az utóbbi az `Active: Downloaded`, `Ready: Compiled`, `Ready - Download` és `Design Mode` szövegeket kezeli, ismeretlen szövegnél `Unknown`. Strict módban `readyForMeasurement` csak megfigyelt `ActiveDownloaded` UI-állapot, azonos deploy/design revision és befejezett művelet mellett igaz. Design mutációk `expectedDesignRevision` + `mutationId` contractot használnak. A graph válaszok `observedAt` és `graphFingerprint` adatot is adnak; tranzakció hiba esetén `rollbackAttempted` és `rollbackSucceeded` jelzi a bizonyosságot. A control wire value JSON-érték: number, boolean, string és array egyaránt reprezentálható; a lokális SigmaStudio Bridge SET contract továbbra is explicit `AUTOMATION_UNVERIFIED` védelem alatt áll.
