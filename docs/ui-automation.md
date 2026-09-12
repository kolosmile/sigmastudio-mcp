# UI Automation

Productionban kizárólag Windows UI Automation selectorok és szükség esetén Clipboard fallback használható. OCR és pixelkoordinátás kattintás nincs implementálva.

A Phase 0 hard gate feladata: SigmaStudio processz, főablak, status bar, Capture Window és Capture sorok megbízható azonosítása. A helyi SigmaStudio 4.7 UI-probe a `captureWindow` AutomationId-t, a `CaptureWndtoolStrip` eszköztárat és a status bar `StatusBar.Pane2` elemét azonosította. A Bridge ezeket accessibility/control-type mintákkal olvassa; OCR és koordináta-alapú automatizálás nincs benne.

Capture-sorok csak akkor kerülnek a snapshotba, ha az accessibility tree `DataItem`, `ListItem`, `TreeItem` vagy `Custom` row-elemeket ad vissza. Üres lista esetén a Bridge jelzi, hogy a selector elérhető-e, de nem gyárt mesterséges kommunikációs eseményt.
