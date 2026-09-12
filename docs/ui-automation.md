# UI Automation

Productionban kizárólag Windows UI Automation selectorok és bizonyított Clipboard fallback használható. OCR és pixelkoordinátás kattintás nincs implementálva.

A Phase 0 hard gate feladata: SigmaStudio processz, főablak, status bar, Capture Window és Capture sorok megbízható azonosítása. A helyi SigmaStudio 4.7 UI-probe a `captureWindow` AutomationId-t, a `CaptureWndtoolStrip` eszköztárat és a status bar `StatusBar.Pane2` elemét azonosította. A Bridge ezeket accessibility/control-type mintákkal olvassa; OCR és koordináta-alapú automatizálás nincs benne.

Capture-sorok elsődlegesen az accessibility tree `DataItem`, `ListItem`, `TreeItem` vagy `Custom` row-elemeiből olvashatók. Ha nincs row, a Bridge csak a helyi UI-probe által bizonyított Copy/Copy All `InvokePattern` után próbál Clipboard-szöveget olvasni; nincs általános `SendKeys`, OCR vagy koordináta-fallback. A klasszikus helyi 4.7 buildben Copy actiont a probe nem talált, ezért a jelenlegi válasz `CAPTURE_COPY_ACTION_UNAVAILABLE`, nem fiktív 0-kommunikáció.

Az observer a Capture snapshotot longest-common-prefix alapján delta-olja. Azonos egymást követő sorok külön események maradnak; rövidebb lista clear/reset eseményként új snapshot-generációt indít.
