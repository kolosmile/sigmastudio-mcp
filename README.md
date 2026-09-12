# SigmaStudio ADAU1701 MCP Server

Helyi, Windows-only MCP control plane klasszikus SigmaStudio 4.7 + ADAU1701 környezethez. A repository egy buildelhető Phase 1–6 vertical slice-t tartalmaz:

- `SigmaStudio.Mcp.Host`: Streamable HTTP MCP szerver `127.0.0.1:8766/mcp` címen;
- `SigmaStudio.Bridge`: külön Windows Bridge Named Pipe IPC-vel, STA dispatcherrel, UI Automation megfigyelővel és késői SigmaStudioServer DLL betöltéssel;
- `SigmaStudio.Core`: revision protection, idempotent mutation, lock, path policy és in-memory backend;
- `SigmaStudio.Graph`: tolerant export parser + graph validator;
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

Alapértelmezésben az alkalmazás biztonságos in-memory backenddel indul, így SigmaStudio és USBi nélkül is kipróbálható. Valós Bridge használatához:

```powershell
dotnet run --project src/SigmaStudio.Bridge -- --sigmaStudioServerPath="C:\Program Files\Analog Devices\SigmaStudio\Analog.SigmaStudioServer.dll"
```

Az MCP host konfigurációjában állítsd a `SigmaStudio:Backend` értékét `Bridge`-re. A proprietary ADI DLL-t tilos a repositoryba másolni; az adapter csak helyi telepítésből tölti be reflectionnel.

## Biztonsági alapok

Az MCP host kizárólag loopbackre bindol, a Host/Origin értékeket validálja, a Bridge pipe aktuális Windows userre ACL-ezett, a raw parameter/register API pedig alapértelmezésben nem jelenik meg. `.dspproj` fájl közvetlen módosítása nincs a kódban.

## Állapot

Az in-memory backend és a protokoll/graph/catalog tesztek működnek. A valódi SigmaStudio 4.7 interop, Capture Window selectorok és USBi/HIL tesztekhez telepített SigmaStudio, azonos privilege level és fizikai hardver szükséges; ezeket a dokumentáció külön, opt-in fázisként kezeli.

Részletes terv: [SigmaStudio ADAU1701 MCP Server – fejlesztési terv és műszaki specifikáció](SigmaStudio%20ADAU1701%20MCP%20Server%20%E2%80%93%20fejleszt%C3%A9si%20terv%20%C3%A9s%20m%C5%B1szaki%20specifik%C3%A1ci%C3%B3.md).
