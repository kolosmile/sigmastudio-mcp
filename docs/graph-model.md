# Graph model

Az alkalmazás szemantikai graphot ad vissza, nem pixelpontos GUI reprodukciót. Az elsődleges production adatforrás a SigmaStudio `LINK → COMPILE → EXPORT_SYSTEM_FILES` kimenete. A parser tolerant reader: ismeretlen XML node-ot nem tekint hibának, nem támaszkodik sibling sorrendre, és unknown mezőket jelzi.

Live export esetén a `Schematic` XML adja a modulokat, algoritmusokat, vezérlőleírásokat és paramétercímeket, a `*_NetList.xml` pedig a blokkok közti `LinkN` topológiát. A normalizáló vizuális cellánként csoportosítja az algoritmusokat. A blokk `id` és a kapcsolat `id` determinisztikus SHA-256 alapú azonosító; a `catalogId` külön típusazonosító.

A graph válasz `observedAt` és `graphFingerprint` mezői az utolsó observationt jelzik. A `PinRefDto.Block` kompatibilitás miatt objectName-et is tartalmaz, a `BlockId` pedig a stabil blokkazonosítót.

A `.dspproj` közvetlen módosítása és reverse engineeringje tiltott. A fixture parser a schema-változások regressziótesztjeinek alapja.
