# Architecture

Az alkalmazás két processz:

```text
MCP client → SigmaStudio.Mcp.Host (.NET) → Windows Named Pipe → SigmaStudio.Bridge (STA) → SigmaStudioServer/UIA → SigmaStudio 4.7
```

Az MCP host tartja a design/runtime/deployed revisioneket, a global operation lockot, a path policyt és a magas szintű tool contractokat. A Bridge egyetlen dedicated STA threaden futtat minden COM/reflection/UI Automation műveletet. A Bridge összeomlása ezért nem állítja le a hostot.

Az ADI assemblyt a `SigmaStudioServerAdapter` reflectionnel tölti be a helyi installból. Exact object- és algorithm-nevek runtime discovery nélkül nincsenek hardcode-olva.
