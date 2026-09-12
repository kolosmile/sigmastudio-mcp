# UI Automation

Productionban kizárólag Windows UI Automation selectorok és bizonyított Clipboard fallback használható. OCR és képernyő-koordinátákból kitalált kattintás nincs implementálva. A klasszikus, owner-drawn Capture grid kivételként a UIA által adott `treeViewAdv1` vezérlő-geometriát és annak szöveges oszloprészét használja; ez nem hardcoded képernyőpont, hanem a helyi SigmaStudio 4.7 profilban bizonyított vezérlőút.

A Phase 0 hard gate feladata: SigmaStudio processz, főablak, status bar, Capture Window és Capture sorok megbízható azonosítása. A helyi SigmaStudio 4.7 UI-probe a `captureWindow` AutomationId-t, a `CaptureWndtoolStrip` eszköztárat és a status bar `StatusBar.Pane2` elemét azonosította. A Bridge ezeket accessibility/control-type mintákkal olvassa; OCR és koordináta-alapú automatizálás nincs benne.

Capture-sorok elsődlegesen az accessibility tree `DataItem`, `ListItem`, `TreeItem` vagy `Custom` row-elemeiből olvashatók. Ha nincs row, a Bridge a bizonyított selected-range útvonalat használja: `Home` + `Shift+End`, jobb klikk a táblázat szöveges részén, a kontextusmenü megjelenésének megvárása, majd `Down` + `Enter` a `Copy to clipboard` aktiválásához. A helyi 4.7 profilon ez 131,122 karakteres, 12 `Block Write` sort tartalmazó clipboard-szöveget adott; a parser ebből strukturált Capture rekordokat készít. A Bridge nem használ OCR-t és nem olvas képernyőképet.

Az observer a Capture snapshotot longest-common-prefix alapján delta-olja. Azonos egymást követő sorok külön események maradnak; rövidebb lista clear/reset eseményként új snapshot-generációt indít.
