# SigmaStudio 4.7 runtime findings

This file records the 2026-09-12 local SigmaStudio 4.7 observation.

Required evidence before enabling the production Bridge:

- installed SigmaStudio version and executable path;
- `Analog.SigmaStudioServer.dll` path and discovered public type/method surface;
- successful `bridge.ping` and capability handshake;
- status bar selector and observed values;
- Capture Window selector, row extraction and clipboard fallback;
- verified `setControlValue` write/readback on a disposable ADAU1701 project.

Observed evidence:

- `SStudio.exe` is installed under `C:\Program Files\Analog Devices\SigmaStudio 4.7` and `Analog.SigmaStudioServer.dll` is version 4.7.0.1827.
- The public server signatures were reflected without invoking undocumented writes: `EXPORT_SYSTEM_FILES`, `REMOVE_OBJECT`, `CONNECT_OBJECT`, `DISCONNECT_OBJECT`, `INSERT_BLOCKOBJECT[_POINT]`, `GET_OBJECT_PROPERTY`, and `SET_OBJECT_PROPERTY` are present.
- A real export produced `*_NetList.xml`, schematic XML, `.params`, generated headers, HEX and transfer-buffer files. The observed NetList contained 30 algorithms and 94 `LinkN` references; the normalized visual graph contained 28 blocks and 53 connections.
- Two live exports produced identical block and connection IDs. `Mute1`, `Mute2`, and `Mute3` were observed in the export with `Mute=0`.
- The UI probe now selects the actual `SStudio.exe` top-level window by process id; this avoids accidentally selecting an Edge window whose tab title contains “SigmaStudio”. The status probe reads `StatusBar.Pane2` and preserves the raw UI text.
- The classic 4.7 Capture tree is `captureWindow` → `panel1`/`panel2` → `treeViewAdv1`. `treeViewAdv1` exposes no UIA row children and no supported UIA patterns. The toolbar `CaptureWndtoolStrip` exposes only the observed Clear/Show Columns/Display Sequence Window controls; no Copy/Copy All InvokePattern was found.
- The classic grid is owner-drawn, so it has no per-row UIA elements. The verified local route is: focus `treeViewAdv1`, click its text-column area, send `Home` followed by `Shift+End`, right-click in the text area, wait for the context menu, then send `Down` and `Enter` on the `Copy to clipboard` item. The clipboard contained 131,122 characters and 12 `Block Write` rows in the disposable local project. Production parses that selected-range clipboard text into structured Capture entries, restores the prior clipboard/foreground window, and retries when the clipboard format is not text.
- The local reflection signature is `GET_OBJECT_PROPERTY(string opcode, string objectName, object[]& getPropVal, object[] propertyParams) -> bool`. A read-only probe for `Mute1`, algorithm `0`, repeat `0`, control `Mute` passed `propertyParams=[0,0,"Mute"]`, returned `true`, and produced one `System.Boolean` value (`false`). The previous `["Mute"]` parameter shape was incorrect.
- The read-only MCP developer tool is `sigma_property_probe_get_control_value`; it returns the reflected signature, object name, opcode, property parameters, return value, CLR types and values.
- A disposable-project-only `sigma_property_probe_set_control_value` developer probe is available behind the HIL gate. On the local `.hil.dspproj`, the verified sequence was SET `Gain1.Gain` to `0.25`, read-after-write `0.25`, a new Capture entry, and restore of the original value. The probe requires `SIGMASTUDIO_MCP_HIL_MUTATION=1`, an exact `SIGMASTUDIO_MCP_HIL_PROJECT` match, and a `.hil.dspproj` or `tests/hil-projects` path.
- The production `block.setControl` and `block.setControls` runtime paths now use the verified three-argument `SET_OBJECT_PROPERTY` contract, refresh the live control state, require typed readback plus a new Capture entry, and attempt restoration when verification fails. The production path is unit-tested against the in-memory backend; a clean-restart SigmaStudio HIL acceptance rerun is still pending because the local fixture currently fails to reopen in SigmaStudio.
- Analog Devices documents the `INSERT_OBJECT[_POINT]` and `INSERT_BLOCKOBJECT[_POINT]` argument as a toolbox `typeName`, and documents `setName` through the separate IScripted interface. The local 4.7 build has the server methods, but no local probe has yet proven the exact installed toolbox type string or a server-side rename mapping; block insertion and rename therefore remain guarded.
- The documented `CONNECT_OBJECT`/`DISCONNECT_OBJECT` methods are present. A disposable live probe confirmed that the server can remove and restore some existing edges, but the exported NetList `P<n>` numbers are not a sufficient server pin-index contract. The supplied two-IC design also becomes non-exportable when mandatory internal edges are removed, so structural disconnect/reconnect and save-close-reopen acceptance remain pending a dedicated small disposable Input → Gain → Output fixture.

Still unverified: final clean-restart HIL acceptance of the production control-write path, block insertion/rename, and structural mutation acceptance on a dedicated disposable fixture. Structural write operations remain explicitly guarded.

Primary ADI references used for the contract:

- [SigmaStudioServer property interface](https://wiki.analog.com/resources/tools-software/sigmastudio/usingsigmastudio/scripting/server)
- [Sample `setControlValue` scripts](https://wiki.analog.com/resources/tools-software/sigmastudio/usingsigmastudio/scripting/iscripted_samples)
- [Capture Window](https://wiki.analog.com/resources/tools-software/sigmastudiov2/developmentenvironment/capturewinow/capture_window)
- [Capture Output Data](https://wiki.analog.com/resources/tools-software/sigmastudiov2/usingsigmastudio/captureoutputdata)
