# UI Automation

Productionban kizárólag Windows UI Automation selectorok és bizonyított Clipboard fallback használható. OCR és képernyő-koordinátákból kitalált kattintás nincs implementálva. A Capture observer kódja megmaradt későbbi opcionális használatra, de jelenleg ki van kapcsolva.

A Phase 0 hard gate feladata: SigmaStudio processz, főablak és status bar megbízható azonosítása. A Capture Window azonosítási kódja megmarad későbbi opcionális használatra, de jelenleg nem fut a Bridge snapshot-útvonalán.

Capture-sorok olvasási és clipboard-fallback kódja megmaradt a későbbi opcionális használathoz, de jelenleg nem fut a Bridge/runtime útvonalon.

A Capture válasz szerializációs kódja megmaradt későbbi használatra, de a Capture toolok jelenleg `CAPTURE_UNAVAILABLE` hibát adnak.

Az observer Capture-delta kódja megmaradt későbbi használatra, de jelenleg nincs meghívva a normál snapshot-útvonalon.
