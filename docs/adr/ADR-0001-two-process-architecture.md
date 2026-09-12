# ADR-0001: two-process architecture

Status: accepted

Az MCP host és az STA/UI Automation + SigmaStudioServer Bridge külön processzben fut. Ez izolálja az interopot és a SigmaStudio crash-t a hosttól.
