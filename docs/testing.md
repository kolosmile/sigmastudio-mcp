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
