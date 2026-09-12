# Testing

Build gate:

```text
dotnet restore SigmaStudioMcp.sln
dotnet build SigmaStudioMcp.sln -c Release
dotnet test SigmaStudioMcp.sln -c Release
```

Hardware nélküli tesztek az in-memory backenddel, graph fixture-ral és catalog fixture-ral futnak. A `Category=HIL` tesztek nem indulnak automatikusan; valódi SigmaStudio 4.7 + USBi + ADAU1701 környezetben külön kell futtatni őket.

## Ellenőrzött SigmaStudio 4.7 HIL lépések

A SigmaStudio 4.7 `Analog.SigmaStudioServer.dll` .NET Framework WCF-függősége miatt a bridge live változata net48:

```powershell
dotnet build src/SigmaStudio.Bridge -c Release -f net48
& .\src\SigmaStudio.Bridge\bin\Release\net48\SigmaStudio.Bridge.exe --sigmaStudioServerPath="C:\Program Files\Analog Devices\SigmaStudio 4.7\Analog.SigmaStudioServer.dll"
```

A 2026-09-12-i helyi futtatási ellenőrzésen a bridge példányosította a 4.7.0.1827 szervert, a UI Automation megtalálta az aktív SigmaStudio ablakot, majd az MCP hoston keresztül a `sigma_compile`, `sigma_link` és `sigma_download` műveletek sikeresek voltak. A visszaolvasott állapot `ActiveDownloaded`, `readyForMeasurement=true` lett. A projektfájl mentése nem történt.

## Live graph acceptance

The opt-in HIL test `Live_graph_extraction_is_opt_in_and_returns_stable_topology` calls the running Bridge, requests `graph.refreshLive`, and verifies non-empty topology plus stable IDs across two exports. It does not save the project.

```powershell
$env:SIGMASTUDIO_MCP_HIL = "1"
dotnet test tests/SigmaStudio.IntegrationTests/SigmaStudio.IntegrationTests.csproj -c Release --filter "Category=HIL"
```

The live parser uses `EXPORT_SYSTEM_FILES`, `*_NetList.xml` for topology, and the schematic XML for module controls and parameters. If export parsing fails, the Bridge returns `GRAPH_EXTRACTION_FAILED` and does not fabricate a graph.

The non-HIL suite also covers deterministic export IDs, graph fingerprints, revision-guarded mutations, transaction diff/rollback, and the accessibility-based Capture observer. The installed Toolbox catalog is intentionally reported as unavailable unless a runtime enumeration is verified.

## Disposable control-write HIL

The opt-in mutation gate exercises the production `sigma_block_set_control` path end to end: it reads the original `Gain1.Gain`, writes `0.25` through `SET_OBJECT_PROPERTY`, verifies the live readback, observes a new Capture entry, and restores the original value. The runtime only reports success after the readback and Capture checks pass; a failed verification attempts an explicit restore. The project must already be open in SigmaStudio and the test does not save it.

```powershell
$env:SIGMASTUDIO_MCP_HIL = "1"
$env:SIGMASTUDIO_MCP_HIL_MUTATION = "1"
$env:SIGMASTUDIO_MCP_HIL_PROJECT = "C:\path\to\control-write-test.hil.dspproj"
$env:SIGMASTUDIO_MCP_HIL_NEW_VALUE = "0.25"
dotnet test tests/SigmaStudio.IntegrationTests/SigmaStudio.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~MutationHilTests.Disposable_control_write_is_read_back_captured_and_restored"
```

The adapter converts exported graph pin indices (`P1`, `P2`, ...) to the documented zero-based `CONNECT_OBJECT`/`DISCONNECT_OBJECT` server arguments. This mapping was verified on a disposable ADAU1701 stereo Input → Output fixture. Structural acceptance still requires a valid intermediate graph: on ADAU1701, disconnecting a mandatory output edge makes `COMPILE`/`EXPORT_SYSTEM_FILES` fail, so that case is not reported as a successful live structural mutation. The large two-IC design remains suitable for graph/control observation, but is not accepted as structural proof.

Structural tests are separately gated with `SIGMASTUDIO_MCP_HIL_STRUCTURAL=1`, so the control-write HIL command above cannot accidentally mutate a production-sized project.

## Capture clipboard HIL

The opt-in Capture test verifies the local 4.7 owner-drawn grid route. It selects the visible range, right-clicks in the text area, waits for the context menu, invokes `Down` + `Enter`, parses the clipboard into structured entries, and requires a non-empty selected-range result.

MCP Capture responses use compact structured JSON by default. Large `rawText` and `rawColumns` fields are omitted and represented by length/SHA-256 metadata; pass `includeRaw=true` to `sigma_capture_get` or `sigma_capture_wait` only when the raw fields are explicitly needed.

```powershell
$env:SIGMASTUDIO_MCP_HIL_CAPTURE = "1"
dotnet test tests/SigmaStudio.IntegrationTests/SigmaStudio.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~CaptureHilTests.Selected_capture_row_copy_is_structured_and_non_empty"
```
