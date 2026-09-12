# Graph model

Az alkalmazás szemantikai graphot ad vissza, nem pixelpontos GUI reprodukciót. Az elsődleges production adatforrás a SigmaStudio `LINK → COMPILE → EXPORT_SYSTEM_FILES` kimenete. A parser tolerant reader: ismeretlen XML node-ot nem tekint hibának, nem támaszkodik sibling sorrendre, és unknown mezőket jelzi.

A `.dspproj` közvetlen módosítása és reverse engineeringje tiltott. A fixture parser a schema-változások regressziótesztjeinek alapja.
