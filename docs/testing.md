# Testing

Build gate:

```text
dotnet restore SigmaStudioMcp.sln
dotnet build SigmaStudioMcp.sln -c Release
dotnet test SigmaStudioMcp.sln -c Release
```

Hardware nélküli tesztek az in-memory backenddel, graph fixture-ral és catalog fixture-ral futnak. A `Category=HIL` tesztek nem indulnak automatikusan; valódi SigmaStudio 4.7 + USBi + ADAU1701 környezetben külön kell futtatni őket.
