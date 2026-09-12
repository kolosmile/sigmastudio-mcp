# SigmaStudio ADAU1701 MCP Server

Helyi, Windows-only MCP control plane klasszikus SigmaStudio 4.7 + ADAU1701 környezethez. A repository egy buildelhető Phase 1–6 vertical slice-t tartalmaz:

- `SigmaStudio.Mcp.Host`: Streamable HTTP MCP szerver `127.0.0.1:8766/mcp` címen;
- `SigmaStudio.Bridge`: külön Windows Bridge Named Pipe IPC-vel, STA dispatcherrel, UI Automation megfigyelővel és késői SigmaStudioServer DLL betöltéssel; a SigmaStudio 4.7 interophoz net48 buildet használ;
- `SigmaStudio.Core`: revision protection, idempotent mutation, lock, path policy és in-memory backend;
- `SigmaStudio.Graph`: tolerant generic parser plus a real SigmaStudio `Schematic` + `*_NetList.xml` export normalizer with deterministic block/connection IDs;
- `SigmaStudio.Catalog`: offline, provenance-bearing ADAU1701 katalógus;
- `SigmaStudio.Cli`: `doctor` és `ui-dump` diagnosztikai parancsok.

## Gyors indítás

Előfeltétel: Windows és .NET 8 SDK. A specifikáció .NET 10-et céloz; a jelenlegi gépen csak a .NET 8 SDK állt rendelkezésre, ezért a kód net8.0-kompatibilis és .NET 10-re közvetlenül targetálható.

```powershell
dotnet restore SigmaStudioMcp.sln
dotnet build SigmaStudioMcp.sln -c Release
dotnet test SigmaStudioMcp.sln -c Release
dotnet run --project src/SigmaStudio.Mcp.Host
```

Health check: `http://127.0.0.1:8766/health`. MCP endpoint: `http://127.0.0.1:8766/mcp`.

Alapértelmezésben az alkalmazás biztonságos in-memory backenddel indul, így SigmaStudio és USBi nélkül is kipróbálható. Valós Bridge használatához (a SigmaStudio 4.7 szerver DLL-je .NET Framework WCF-t használ, ezért a live bridge net48):

```powershell
dotnet build src/SigmaStudio.Bridge -c Release -f net48
& .\src\SigmaStudio.Bridge\bin\Release\net48\SigmaStudio.Bridge.exe --sigmaStudioServerPath="C:\Program Files\Analog Devices\SigmaStudio 4.7\Analog.SigmaStudioServer.dll"
```

Az MCP host konfigurációjában állítsd a `SigmaStudio:Backend` értékét `Bridge`-re. A proprietary ADI DLL-t tilos a repositoryba másolni; az adapter csak helyi telepítésből tölti be reflectionnel.

## Biztonsági alapok

Az MCP host kizárólag loopbackre bindol, a Host/Origin értékeket validálja, a Bridge pipe aktuális Windows userre ACL-ezett, a raw parameter/register API pedig alapértelmezésben nem jelenik meg. `.dspproj` fájl közvetlen módosítása nincs a kódban.

## Állapot

Az in-memory backend és a protokoll/graph/catalog tesztek működnek. A SigmaStudio 4.7 interopot a helyi környezetben ellenőriztük: a szerver DLL példányosítása, UI Automation ablakfelderítés, `LINK`, `COMPILE`, `DOWNLOAD`, valamint a valós `EXPORT_SYSTEM_FILES` alapú live graph extraction sikeres volt. A jelenlegi projekt exportja 28 normalizált vizuális blokkot és 53 kapcsolatot adott vissza; két egymást követő export azonos blokk- és kapcsolat-ID-kat eredményezett. A graph transaction, export-alapú checkpoint/undo rollback, UI status és accessibility Capture observer bekerült. A SigmaStudio 4.7 `GET_OBJECT_PROPERTY` control readback és a `SET_OBJECT_PROPERTY` write opcode még nincs bizonyítva, ezért a Bridge ezt nem jelenti sikeresnek és nem engedélyez guessed control write-ot.

Részletes terv: [SigmaStudio ADAU1701 MCP Server – fejlesztési terv és műszaki specifikáció](SigmaStudio%20ADAU1701%20MCP%20Server%20%E2%80%93%20fejleszt%C3%A9si%20terv%20%C3%A9s%20m%C5%B1szaki%20specifik%C3%A1ci%C3%B3.md).
