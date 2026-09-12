# UI Automation

Productionban kizárólag Windows UI Automation selectorok és szükség esetén Clipboard fallback használható. OCR és pixelkoordinátás kattintás nincs implementálva.

A Phase 0 hard gate feladata: SigmaStudio processz, főablak, status bar, Capture Window és Capture sorok megbízható azonosítása. A jelenlegi adapter jelzi, hogy a selector profile még nincs validálva; HIL használat előtt a `SigmaStudio.UiProbe` eszközzel profil készítendő.
