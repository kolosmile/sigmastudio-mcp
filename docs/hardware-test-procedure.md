# Hardware test procedure

1. Telepítsd SigmaStudio 4.7 x64-et és indítsd azonos Windows user/integrity level alatt a Bridge-dzsel.
2. Ellenőrizd `sigmastudio-mcp doctor`-ral az SStudio processzt, a DLL-t és a pipe connectivityt.
3. A Capture jelenleg ki van kapcsolva. A control write engedélyezésének feltétele a verified opcode és a live readback.
4. Futtasd a Block control PoC-t Input → Gain → Output projekten.
5. Futtasd a Graph mutation PoC-t, majd save → close → reopen graph ellenőrzést.
6. HIL acceptance: 100 runtime control update és 20 deploy ciklus; a Capture/REW ellenőrzési kör jelenleg nincs a workflow-ban.

The live graph acceptance command is documented in `docs/testing.md`. It never saves the open project and requires `SIGMASTUDIO_MCP_HIL=1`.
