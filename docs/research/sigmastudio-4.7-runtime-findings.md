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
- The UI probe found `captureWindow`, `CaptureWndtoolStrip`, and status text `Active: Downloaded` in `StatusBar.Pane2`.

Still unverified: the exact `SET_OBJECT_PROPERTY`/control opcode contract and property readback arguments. The adapter therefore reports `CONTROL_READBACK_UNAVAILABLE` when the read-only probe returns no value and keeps block control writes disabled rather than guessing an opcode.
