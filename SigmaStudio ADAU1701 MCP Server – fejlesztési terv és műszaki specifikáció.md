# SigmaStudio ADAU1701 MCP Server
## Fejlesztési terv, architektúra és implementációs specifikáció

**Dokumentum státusza:** implementációra kész specifikáció  
**Célplatform:** Analog Devices ADAU1701 + klasszikus SigmaStudio  
**Elsődleges SigmaStudio-verzió:** SigmaStudio 4.7 x64  
**Cél operációs rendszer:** Windows x64  
**MCP transport:** localhost Streamable HTTP  
**Elsődleges implementációs nyelv:** C#  
**Dokumentum dátuma:** 2026-09-12

---

# 1. Cél

A projekt célja egy olyan helyi MCP szerver létrehozása, amelyen keresztül AI agentek teljes értékűen tudják vezérelni a klasszikus Analog Devices SigmaStudio környezetet és azon keresztül egy ADAU1701 DSP-t.

A végső felhasználási eset:

```text
Agent
  │
  ├── SigmaStudio MCP
  │     ├── projekt kezelése
  │     ├── DSP blokkdiagram vizsgálata
  │     ├── blokkok módosítása
  │     ├── paraméterek módosítása
  │     ├── compile / download
  │     ├── Capture Window figyelése
  │     └── Active: Downloaded ellenőrzése
  │
  └── REW API / MCP
        ├── mérés
        ├── eredmény kiolvasása
        └── további optimalizáció
```

Az agentnek így képesnek kell lennie például az alábbi iteráció önálló végrehajtására:

```text
DSP aktuális állapotának lekérése
→ paraméter módosítása
→ tényleges SigmaStudio write ellenőrzése
→ ready_for_measurement ellenőrzése
→ REW mérés
→ eredmény elemzése
→ új DSP-paraméter
→ új mérés
→ ...
```

A SigmaStudio MCP **nem tartalmaz REW-integrációt**. A két rendszer különálló marad; az agent koordinálja őket.

---

# 2. Kötelező funkcionális scope

A v1 kiadásnak támogatnia kell:

## 2.1 Projektműveletek

- új ADAU1701 projekt létrehozása;
- meglévő `.dspproj` projekt megnyitása;
- projekt mentése;
- Save As;
- projekt bezárása;
- checkpoint/backup létrehozása;
- Undo;
- Redo;
- projekt aktuális állapotának lekérése.

## 2.2 Blokkdiagram

- aktuális schematic szemantikai blokkdiagramjának kiolvasása;
- blokkok listázása;
- blokk típusának és algoritmusának azonosítása;
- blokk input/output pinek listázása;
- blokk paramétereinek/controljainak lekérése;
- blokkok hozzáadása;
- blokkok törlése;
- blokkok átnevezése, ahol ezt SigmaStudio támogatja;
- kapcsolatok listázása;
- kapcsolat létrehozása;
- kapcsolat törlése.

A „blokkdiagram kiolvasása” alatt elsősorban a **szemantikai gráf** értendő, nem a GUI pixelpontos reprodukciója.

## 2.3 Paraméterkezelés

Elsődleges mechanizmus:

```text
SigmaStudio control/property
→ SET_OBJECT_PROPERTY / ObjectSetProperties("setControlValue")
```

Másodlagos, haladó mechanizmus:

```text
PARAMETER_READ*
PARAMETER_WRITE*
PARAMETER_SAFELOAD*
REGISTER_READ*
REGISTER_WRITE*
```

A magas szintű agent API-ban a nyers memória/regiszter műveletek alapértelmezés szerint legyenek letiltva.

## 2.4 Build és DSP-programozás

- Link;
- Compile;
- Download;
- Link + Compile + Download;
- hibák felismerése;
- timeout;
- végállapot ellenőrzése.

A SigmaStudioServer hivatalosan biztosít többek között `NEW_PROJECT`, `OPEN_PROJECT`, `SAVE_PROJECT`, `LINK`, `COMPILE`, `DOWNLOAD`, `COMPILE_PROJECT`, parameter/register read/write, block insert/remove/connect, object property és export API-kat. A kliensnek és a SigmaStudiónak ugyanazon a PC-n kell futnia. citeturn119067view0

## 2.5 SigmaStudio Capture Window

A rendszernek ki kell tudnia olvasni az új Capture Window bejegyzéseket.

A Capture Window az ADI dokumentáció szerint valós időben megmutatja a hardver felé küldött adatokat, beleértve a link/compile/download adatokat, register read/write műveleteket és schematic control változásokat. citeturn378160search0

A klasszikus SigmaStudio számára nincs dokumentált programozott Capture Window API. Egy ADI EngineerZone verified válasz szerint a Capture Window tartalma közvetlenül nem érhető el programból. Emiatt ezt a funkciót Windows UI Automation adapterrel kell megvalósítani. citeturn653043search0

## 2.6 SigmaStudio státusz

Ki kell olvasni legalább az alábbi állapotokat:

```text
100% Active: Downloaded
100% Ready: Compiled
Ready - Download
Design Mode
ismeretlen / hibaállapot
```

Sikeres letöltés után az ADI dokumentáció szerint a státusz `100% Active: Downloaded`; compile-time módosítás esetén `Design Mode`, majd új letöltés után ismét `Active: Downloaded`. citeturn786035search0turn786035search1

---

# 3. Nem része a v1-nek

A következő funkciókat nem kell az első stabil verzióban megvalósítani:

- REW vezérlése;
- DSP kommunikáció saját USB/I²C/SPI driverrel;
- `.dspproj` formátum reverse engineeringje;
- képernyőkép/OCR alapú GUI-felismerés;
- pixelkoordinátás egérkattintás production kódban;
- SigmaStudio+ támogatás;
- ADAU1701-en kívüli DSP-k hivatalos támogatása;
- több SigmaStudio instance egyidejű kezelése;
- távoli hálózatról elérhető MCP;
- teljes SigmaStudio GUI távoli reprodukciója.

---

# 4. Források és source-of-truth szabályok

Codex **nem találgathat SigmaStudio objektum-, algoritmus-, parameter- vagy controlneveket**.

Az információforrások prioritási sorrendje:

1. Analog Devices hivatalos SigmaStudio/SigmaDSP Wiki.
2. Analog Devices hivatalos SigmaStudio 4.7 dokumentáció és a telepített SigmaStudio runtime metadata.
3. SigmaStudio által generált Export System Files.
4. Analog Devices EngineerZone verified/ADI employee válaszok.
5. Microsoft hivatalos Windows UI Automation dokumentáció.
6. MCP hivatalos specifikáció és C# SDK dokumentáció.

Blog, fórumbejegyzés vagy harmadik fél által írt példa csak tájékozódásra használható, source-of-truthként nem.

A jelenlegi klasszikus SigmaStudio letöltési oldalon a 4.7 a legújabb hagyományos kiadás, ezért ez legyen a v1 referencia-verzió. citeturn625690search1

Az ADI Toolbox Wiki a SigmaStudio algoritmusok hivatalos részletes dokumentációja; a rendelkezésre álló blokkok a projektben használt DSP-től függnek. A Wiki több blokknál `Cores Supported`, pinek, controlok, algoritmus-részletek és resource usage adatokat is közöl. citeturn536698search0turn536698search3

A block catalog generálásakor minden rekordnak tartalmaznia kell a forrás provenance-adatait.

---

# 5. Alapvető architekturális döntések

## 5.1 Kétprocesszes architektúra

A SigmaStudio integrációt és az MCP HTTP szervert külön processzbe kell helyezni.

```text
                         Agent
                           │
                           │ MCP Streamable HTTP
                           ▼
┌────────────────────────────────────────────┐
│ SigmaStudio.Mcp.Host                       │
│ .NET 10                                    │
│                                            │
│ MCP tools/resources                        │
│ domain logic                               │
│ graph model                                │
│ block catalog                              │
│ locking/revision management                │
└─────────────────────┬──────────────────────┘
                      │
                      │ local Named Pipe
                      ▼
┌────────────────────────────────────────────┐
│ SigmaStudio.Bridge                         │
│ .NET Framework 4.8                         │
│ dedicated STA automation thread            │
│                                            │
│ SigmaStudioServer adapter                  │
│ IScripted script runner                    │
│ Windows UI Automation observer             │
│ Capture Window adapter                     │
└─────────────────────┬──────────────────────┘
                      │
             ┌────────┴─────────┐
             ▼                  ▼
       SigmaStudioServer     Windows UIA
             │                  │
             └────────┬─────────┘
                      ▼
                SigmaStudio 4.7
                      │
                    USBi
                      │
                   ADAU1701
```

### Indoklás

A SigmaStudio klasszikus .NET Framework alkalmazás. A modern MCP SDK ASP.NET Core változata jelenleg .NET 8/9/10 célkeretrendszereket támogat. citeturn279317search2

A két processz:

- izolálja a régi SigmaStudio interopot;
- külön kezeli az STA/UI Automation követelményeket;
- nem kényszeríti a SigmaStudio assemblyt modern .NET runtime-ba;
- megakadályozza, hogy SigmaStudio összeomlása magával vigye az MCP hostot;
- CI-ben ADI binary nélkül is buildelhetővé teszi a projektet.

## 5.2 Az ADI DLL nem kerülhet repositoryba

`Analog.SigmaStudioServer.dll`:

- nem commitolható;
- nem redisztribuálható a projekt részeként;
- runtime során a helyi SigmaStudio install könyvtárából töltendő.

A Bridge használjon late binding/reflection alapú loadert.

Elsődleges osztály:

```text
Analog.SigmaStudioServer.SigmaStudioServer
```

A hivatalos ADI példák ezt a típust példányosítják. citeturn999333search2turn999333search6

## 5.3 Egyetlen STA automation thread

A Bridge minden SigmaStudioServer-, scripting-, UI Automation- és Clipboard-műveletét **egyetlen dedicated STA threaden** hajtsa végre.

ThreadPoolról párhuzamos SigmaStudio hívás tilos.

---

# 6. MCP host technológia

Használandó:

```text
.NET 10
ASP.NET Core
ModelContextProtocol.AspNetCore 2.2.x
```

A hivatalos C# MCP SDK támogatja az ASP.NET Core HTTP szervert. citeturn119067view4turn279317search2

A projekt célozza a jelenlegi `2026-07-28` MCP protokollt, de ahol az SDK automatikusan biztosít visszafelé kompatibilitást, azt nem kell kikapcsolni. A 2026-07-28 revízió stateless protokollmagot vezetett be. citeturn568866search0

Transport:

```text
http://127.0.0.1:8766/mcp
```

Kötelező:

- csak loopback bind;
- Host header validáció;
- Origin validáció;
- `0.0.0.0` használata tilos alapkonfigurációban.

A fizikai DSP állapota továbbra is globálisan stateful; az MCP transport stateless volta ettől független.

---

# 7. Repository struktúra

```text
SigmaStudioMcp/
│
├── src/
│   ├── SigmaStudio.Mcp.Host/
│   ├── SigmaStudio.Bridge/
│   ├── SigmaStudio.Contracts/
│   ├── SigmaStudio.Core/
│   ├── SigmaStudio.Graph/
│   ├── SigmaStudio.Catalog/
│   └── SigmaStudio.Cli/
│
├── tools/
│   ├── SigmaStudio.CatalogBuilder/
│   └── SigmaStudio.UiProbe/
│
├── tests/
│   ├── SigmaStudio.Core.Tests/
│   ├── SigmaStudio.Graph.Tests/
│   ├── SigmaStudio.Catalog.Tests/
│   ├── SigmaStudio.Bridge.Tests/
│   ├── SigmaStudio.Mcp.ContractTests/
│   ├── SigmaStudio.IntegrationTests/
│   └── fixtures/
│
├── data/
│   └── catalog/
│       └── adau1701/
│
├── docs/
│   ├── architecture.md
│   ├── mcp-tools.md
│   ├── bridge-protocol.md
│   ├── graph-model.md
│   ├── block-catalog.md
│   ├── ui-automation.md
│   ├── testing.md
│   ├── hardware-test-procedure.md
│   ├── troubleshooting.md
│   ├── source-register.md
│   └── adr/
│
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── README.md
├── CHANGELOG.md
└── SigmaStudioMcp.sln
```

---

# 8. Bridge IPC

Az MCP Host és Bridge között Windows Named Pipe kommunikáció legyen.

Ne használjon localhost TCP-t.

Pipe:

```text
SigmaStudioMcp.<CurrentUserSidHash>
```

A Pipe ACL kizárólag az aktuális Windows user számára engedélyezze a hozzáférést.

## 8.1 Wire protocol

UTF-8 JSON, 32 bites little-endian message length prefixszel.

Request:

```json
{
  "id": "uuid",
  "method": "project.open",
  "params": {},
  "timeoutMs": 30000
}
```

Response:

```json
{
  "id": "uuid",
  "ok": true,
  "result": {},
  "error": null,
  "durationMs": 125
}
```

Hiba:

```json
{
  "id": "uuid",
  "ok": false,
  "result": null,
  "error": {
    "code": "PROJECT_OPEN_FAILED",
    "message": "Human readable message",
    "details": {}
  }
}
```

Maximum message size:

```text
16 MiB
```

A Bridge request/response protokoll maradjon belső és verziózott.

Kezdeti handshake:

```text
bridge.get_version
bridge.get_capabilities
bridge.ping
```

---

# 9. SigmaStudioServer adapter

A Bridge a telepített:

```text
Analog.SigmaStudioServer.dll
```

assemblyt reflectionnel töltse be.

Startup:

```text
configured path
    ↓
ha nincs:
SigmaStudio install discovery
    ↓
version validation
    ↓
Assembly.LoadFrom()
    ↓
SigmaStudioServer példány
```

A dependency assemblyket ugyanabból a SigmaStudio install mappából kell feloldani.

A Bridge belső interfésze:

```csharp
ISigmaStudioAutomation
```

Ezt mockolni kell tudni.

Kötelező wrapper műveletek:

```text
NewProject
OpenProject
SaveProject
SaveProjectAs
CloseProject

Link
Compile
Download
CompileProject

SetLoggingMode

InsertObject
InsertBlockObject
RemoveObject
ConnectObject
DisconnectObject

GetObjectProperty
SetObjectProperty

ExportSystemFiles

ParameterRead
ParameterWrite
ParameterSafeloadWrite

RegisterRead
RegisterWrite

Undo
Redo

RunScript
RunScriptFile
```

A hivatalosan dokumentált API-signatúrák implementálásakor az ADI SigmaStudioServer dokumentáció legyen a referencia. citeturn119067view0

---

# 10. IScripted / generált SigmaStudio script használata

A SigmaStudioServer `RUN_SCRIPT` és `RUN_SCRIPT_FILE` funkciója használható olyan esetekben, amelyek a külső API-ból nehézkesek.

Az ADI dokumentáció szerint az IScripted script:

- képes projektet létrehozni;
- objektumot beszúrni;
- objektumokat összekötni;
- objektumot elnevezni;
- algoritmust hozzáadni;
- control értéket módosítani. citeturn659050search0turn659050search6

A `block_add` elsődleges implementációja generált script legyen, ha az adott blokknál ez megbízhatóbb.

A generált script:

1. beszúrja a blokkot;
2. determinisztikus nevet állít;
3. szükség esetén hozzáadja a megfelelő algoritmust;
4. result JSON-t ír egy kontrollált temp fájlba;
5. hibánál szintén strukturált result fájlt ír.

Felhasználói stringet tilos escape nélkül C# scriptbe interpolálni.

A ScriptGenerator rendelkezzen unit tesztekkel minden string escaping esetre.

---

# 11. Új ADAU1701 projekt

A magas szintű MCP tool:

```text
sigma_project_create
```

ne csak üres `NEW_PROJECT()` hívás legyen.

Feladata használható ADAU1701 projekt létrehozása:

```text
new project
→ USBi kommunikációs blokk
→ ADAU1701 hardware block
→ megfelelő hardware kapcsolat
→ schematic létrehozása
→ Save As
```

A pontos hardware object type neveket **runtime discoveryvel kell meghatározni**, nem hardcode-olt feltételezéssel.

Az ADI scripting dokumentáció bizonyítja, hogy hardware configuration blokkok scriptből beszúrhatók és kapcsolhatók. citeturn659050search0

Ha az adott SigmaStudio buildben a teljes bootstrap script nem stabil:

```text
project_create
```

egy konfigurált, előzetesen validált ADAU1701 template projektből készítsen másolatot.

Az MCP szempontjából a két implementáció ugyanazt az API-t adja.

---

# 12. Projekt state model

A Host tartson nyilván külön design és runtime revíziót.

```text
designRevision
runtimeRevision
deployedDesignRevision
```

Példa:

```text
projekt megnyitása:
designRevision = 1
deployedDesignRevision = unknown

block hozzáadása:
designRevision = 2

deploy:
deployedDesignRevision = 2

realtime gain módosítása:
runtimeRevision = 1
designRevision továbbra is 2
```

Normalizált állapotok:

```text
NO_APPLICATION
NO_PROJECT
DESIGN_MODE
READY_COMPILED
ACTIVE_DOWNLOADED
BUSY
ERROR
UNKNOWN
```

`readyForMeasurement` csak akkor lehet `true`, ha:

```text
SigmaStudio state == ACTIVE_DOWNLOADED
AND
nincs folyamatban művelet
AND
nincs ismert deployolatlan design módosítás
```

---

# 13. Blokkdiagram kiolvasása

A `.dspproj` közvetlen módosítása vagy reverse engineeringje tilos.

Elsődleges adatforrás:

```text
LINK / COMPILE
→ EXPORT_SYSTEM_FILES
→ XML / Netlist XML / params parser
```

A klasszikus SigmaStudio exportja paraméterneveket, címeket és értékeket tartalmazó fájlokat generál; későbbi SigmaStudio verziók netlist XML-t is exportálnak. Az ADI SigmaStudio+ migrációs dokumentáció kifejezetten SigmaStudio 4.6+ exportált XML és `_Netlist.xml` fájlokból rekonstruálja a klasszikus projektet. citeturn516936search0turn516936search5turn516936search7

Fallback:

```text
net_list.cir2
.params
_PARAM.h
```

A linker `net_list.cir2` fájlja a Link Window Node List információját tartalmazza. citeturn516936search2

## 13.1 Normalizált Graph DTO

```json
{
  "project": {
    "path": "...",
    "chip": "ADAU1701",
    "sampleRateHz": 48000
  },
  "blocks": [],
  "connections": [],
  "designRevision": 12,
  "freshness": "fresh"
}
```

Block:

```json
{
  "id": "stable-internal-id",
  "objectName": "Gain1",
  "fullObjectName": "Board1.Gain1",
  "catalogId": "volume.linear_gain",
  "displayName": "Gain",
  "algorithms": [],
  "inputs": [],
  "outputs": [],
  "controls": [],
  "parameters": [],
  "position": null
}
```

Connection:

```json
{
  "source": {
    "block": "Gain1",
    "pinIndex": 0,
    "pinName": "Output"
  },
  "target": {
    "block": "Output1",
    "pinIndex": 0,
    "pinName": "Input"
  }
}
```

A GUI pozíció `nullable`.

A graph parser legyen tolerant reader:

- ismeretlen XML node-ot nem tekint hibának;
- XML sorrendre nem támaszkodhat;
- verzióváltozásnál logolja az ismeretlen mezőket;
- minden parser változáshoz fixture regression test kell.

---

# 14. Graph freshness

A `graph_get` támogat:

```text
refresh = cached
refresh = compile
```

### cached

Nem módosítja SigmaStudio állapotát.

Visszaadhat:

```json
"freshness": "stale"
```

### compile

Végrehajt:

```text
LINK
→ COMPILE
→ EXPORT_SYSTEM_FILES
→ parse
```

**DOWNLOAD nincs.**

Ha a compile sikertelen:

- a korábbi cached graph visszaadható;
- egyértelmű `stale=true`;
- a link/compiler hibák szerepeljenek a válaszban.

---

# 15. Block Knowledge Base

Ez külön komponens:

```text
SigmaStudio.Catalog
```

A katalógus alapja az Analog Devices hivatalos SigmaStudio Toolbox Wiki. Az ADI maga ezt jelöli a SigmaStudio algoritmusok részletes dokumentációjának. citeturn536698search0turn659050search8

Runtime alatt ne legyen szükséges internetkapcsolat.

## 15.1 CatalogBuilder

Külön CLI:

```text
SigmaStudio.CatalogBuilder
```

Feladata:

```text
ADI Toolbox index
→ oldalak felfedezése
→ strukturált adatok kinyerése
→ ADAU1701 compatibility
→ validálás
→ catalog JSON generálása
```

Tárolandó mezők:

```json
{
  "id": "...",
  "name": "...",
  "toolboxPath": "...",
  "category": "...",
  "coresSupported": [],
  "adau1701Compatibility": "supported",
  "inputs": [],
  "outputs": [],
  "controls": [],
  "dspParameters": [],
  "growSupported": false,
  "addAlgorithmSupported": false,
  "resourceUsage": {},
  "warnings": [],
  "automation": {},
  "source": {
    "publisher": "Analog Devices",
    "retrievedAt": "...",
    "contentHash": "..."
  }
}
```

Compatibility érték:

```text
supported
unsupported
unknown
```

**Unknown soha nem alakítható automatikusan supported értékké.**

Az `ADAU170x` dokumentációs jelölést a normalizáló réteg ADAU1701-kompatibilis családként kezelheti.

Az explicit negatív megjegyzés mindig erősebb, mint a családszintű kompatibilitás. Például az ADI egyes filtereknél explicit jelzi, hogy egy adott SigmaStudio-verzióban ADAU1701-en nem működnek. citeturn786035search10

## 15.2 Ne másolja le a teljes Wikit

A repository csak:

- strukturált technikai metadata;
- rövid normalizált leírás;
- source provenance;
- source reference

adatokat tároljon.

Teljes ADI Wiki HTML/text mirror ne kerüljön repositoryba.

## 15.3 Installed SigmaStudio verification

A Wiki dokumentáció és a telepített SigmaStudio eltérése esetén:

```text
Wiki = szemantikai source-of-truth
installed SigmaStudio = runtime capability source-of-truth
```

A katalógus ezért tartalmazza:

```text
automationStatus:
  verified
  unverified
  unavailable
```

és:

```text
insertStrategy
insertObjectTypeName
algorithmVariant
```

mezőket.

---

# 16. Block automation discovery

Kell egy automatikus developer verifier.

Egy disposable ADAU1701 projektben:

```text
candidate block
→ script ObjectInsert
→ determinisztikus név
→ szükséges algorithm
→ LINK
→ siker?
→ blokk eltávolítása
```

Siker után:

```text
automationStatus = verified
```

A Wiki címét és toolbox pathját használja candidateként, de sikertelen insert után **nem próbálhat véletlenszerű neveket**.

Ha eltérés van, a telepített SigmaStudio Tree Toolbox/UI metadata vizsgálatával kell meghatározni a pontos nevet.

Minden ilyen eltérést dokumentálni kell:

```text
docs/block-catalog.md
```

---

# 17. Blokk controlok

A fő agent interfész SigmaStudio GUI/control szinten működjön.

Az ADI dokumentálja, hogy a control parameter nevek megjeleníthetők tooltipként, és ezeket használja az `ObjectGetProperties` / `ObjectSetProperties` `getControlValue`/`setControlValue` művelete. citeturn226673search0turn659050search6

Például:

```text
block = Gain1
algorithmIndex = 0
repeatIndex = 0
control = Gain
value = 0.25
```

A backend hívása:

```text
setControlValue
```

Ez legyen előnyben a közvetlen parameter RAM írással szemben.

## 17.1 Range validation

Ha az ADI blokk dokumentáció control tartományt közöl:

```text
min
max
enum
```

a backend validálja.

Agent nem írhat dokumentált tartományon kívüli értéket, kivéve ha az explicit advanced override engedélyezve van.

---

# 18. Nyers parameter/register API

Külön advanced feature flag:

```json
{
  "enableRawParameterAccess": false,
  "enableRawRegisterAccess": false
}
```

Csak engedélyezve jelenjenek meg vagy legyenek használhatók:

```text
sigma_parameter_read
sigma_parameter_write
sigma_parameter_safeload_write
sigma_register_read
sigma_register_write
```

A parameter címeket elsősorban az exportált `.params` / `_PARAM.h` alapján kell meghatározni.

Numeric fixed-point formátumot tilos találgatni.

---

# 19. Capture Window observer

Production implementation:

```text
System.Windows.Automation
```

A Microsoft UI Automation programból hozzáférést biztosít desktop UI elemekhez és azok control patternjeihez. citeturn713082search15turn713082search7

Developer inspectionre használható Microsoft `winapp ui`, Inspect.exe vagy Accessibility Insights. A `winapp ui` szintén UI Automationt használ és Win32/WPF/WinForms alkalmazásokat tud vizsgálni. citeturn119067view5

## 19.1 Tilos

Productionban:

```text
screenshot OCR
pixel coordinate click
image recognition
hardcoded monitor coordinate
```

tilos.

## 19.2 Capture Window megnyitása

Az ADI dokumentáció szerint:

```text
View → Capture Window
```

menüből nyitható meg. citeturn378160search0

A Bridge szükség esetén UI Automation `InvokePattern` segítségével nyissa meg.

## 19.3 Olvasási stratégia

Prioritás:

```text
1. GridPattern / TablePattern
2. TextPattern / ValuePattern
3. accessibility tree children
4. UI Automation context menu → Copy to Clipboard
```

Clipboard fallback esetén:

1. előző clipboard tartalom elmentése;
2. Capture tartalom másolása;
3. feldolgozás;
4. lehetőség szerint az eredeti clipboard visszaállítása.

## 19.4 Capture DTO

Az eredeti tartalom mindig megmaradjon.

```json
{
  "sequence": 1234,
  "timestampObserved": "...",
  "mode": "SafeLoad Write",
  "cellName": "Gain1",
  "parameterName": "...",
  "address": 23,
  "value": 0.5,
  "dataHex": "...",
  "byteCount": 4,
  "rawColumns": {}
}
```

Ha egy oszlop nem parse-olható:

```text
ne vesszen el
→ rawColumns-ban maradjon
```

## 19.5 Cursor

`capture_get` ne sorindexre támaszkodjon.

Minden sorhoz készítsen stabil hash-t:

```text
normalized columns
→ SHA-256
```

A Host tartson ring buffert.

Default:

```text
10 000 Capture entry
```

---

# 20. SigmaStudio UI status observer

A Bridge a SigmaStudio main window status barját figyelje.

A normalizáló:

```text
"100% Active: Downloaded"
    → ACTIVE_DOWNLOADED

"Active: Downloaded"
    → ACTIVE_DOWNLOADED

"100% Ready: Compiled"
    → READY_COMPILED

"Ready - Download"
    → READY_COMPILED

"Design Mode"
    → DESIGN_MODE
```

Az eredeti raw string mindig legyen elérhető.

Selectors:

- AutomationId elsődleges;
- ControlType másodlagos;
- Name csak fallback.

A selectorok verzióprofilban legyenek:

```text
ui-profiles/
  sigmastudio-4.7-en-US.json
```

V1-ben csak angol SigmaStudio UI kötelező.

---

# 21. Deploy folyamat

A magas szintű:

```text
sigma_deploy
```

egyetlen atomic domain operation.

Folyamat:

```text
exclusive hardware lock
        ↓
statusBefore
        ↓
capture cursor
        ↓
LINK
        ↓
COMPILE
        ↓
DOWNLOAD
        ↓
wait for ACTIVE_DOWNLOADED
        ↓
capture delta
        ↓
statusAfter
        ↓
deployedDesignRevision update
```

Siker feltétele:

```text
LINK == success
AND
COMPILE == success
AND
DOWNLOAD == success
AND
UI state == ACTIVE_DOWNLOADED
```

A Capture Window megléte önmagában ne legyen kötelező a deploy sikeréhez.

Ha Capture kiolvasás nem működik:

```json
{
  "success": true,
  "verification": "status-only",
  "warnings": ["CAPTURE_UNAVAILABLE"]
}
```

Strict konfigurációban lehessen ezt hibának venni.

---

# 22. MCP toolkészlet

A toolnevek kapjanak `sigma_` prefixet, hogy REW MCP mellett se legyenek félreérthetők.

## Application

```text
sigma_status
sigma_ready_for_measurement
sigma_diagnostics
```

## Project

```text
sigma_project_create
sigma_project_open
sigma_project_save
sigma_project_save_as
sigma_project_close
sigma_project_checkpoint
sigma_project_undo
sigma_project_redo
sigma_project_export
```

## Graph

```text
sigma_graph_get
sigma_graph_validate
```

## Block knowledge

```text
sigma_block_search
sigma_block_docs
```

## Block manipulation

```text
sigma_block_get
sigma_block_add
sigma_block_remove
sigma_block_rename
sigma_block_get_controls
sigma_block_set_control
sigma_block_set_controls
```

## Connection

```text
sigma_connection_add
sigma_connection_remove
```

## Build/deploy

```text
sigma_link
sigma_compile
sigma_download
sigma_deploy
```

## Capture

```text
sigma_capture_get
sigma_capture_cursor
sigma_capture_wait
```

## Advanced, default disabled

```text
sigma_parameter_list
sigma_parameter_read
sigma_parameter_write
sigma_parameter_safeload_write

sigma_register_read
sigma_register_write
```

---

# 23. Fontos MCP input contractok

Minden design-módosító művelet fogadjon:

```text
expectedDesignRevision
mutationId
```

Példa:

```json
{
  "block": "Gain1",
  "control": "Gain",
  "value": 0.5,
  "expectedDesignRevision": 12,
  "mutationId": "uuid"
}
```

Ha a design közben megváltozott:

```text
STALE_REVISION
```

hibát kell visszaadni.

A `mutationId` eredményét rövid időre cache-elni kell, hogy MCP/HTTP retry ne hozzon létre kétszer blokkot.

Retention:

```text
10 perc
```

---

# 24. Batch control update

Iteratív EQ optimalizáció miatt fontos:

```text
sigma_block_set_controls
```

Példa:

```json
{
  "block": "Param EQ1",
  "changes": [
    {
      "control": "Frequency1",
      "value": 82.5
    },
    {
      "control": "Gain1",
      "value": -3.4
    },
    {
      "control": "Q1",
      "value": 2.1
    }
  ]
}
```

A művelet egy lock alatt fusson.

Lehetőség szerint:

```text
pre-read old values
→ apply
→ failure esetén rollback
```

A response jelezze:

```text
rollbackAttempted
rollbackSucceeded
```

---

# 25. MCP Resources

A toolok mellett legyenek read-only MCP resources:

```text
sigmastudio://status
sigmastudio://project/graph
sigmastudio://catalog/adau1701
sigmastudio://blocks/{catalogId}
sigmastudio://capture/recent
```

A block resource a normalizált ADI dokumentációs adatot adja vissza, ne a teljes Wiki oldalt.

---

# 26. Tool result formátum

Minden művelet strukturált outputot adjon.

```json
{
  "ok": true,
  "operationId": "...",
  "designRevision": 12,
  "runtimeRevision": 91,
  "sigmaStudioState": "ACTIVE_DOWNLOADED",
  "readyForMeasurement": true,
  "warnings": [],
  "data": {}
}
```

Hiba:

```json
{
  "ok": false,
  "operationId": "...",
  "error": {
    "code": "LINK_FAILED",
    "message": "...",
    "details": {}
  }
}
```

Az MCP toolok ne adjanak kizárólag természetes nyelvű eredményt.

---

# 27. Hibakódok

Minimum:

```text
SIGMASTUDIO_NOT_FOUND
SIGMASTUDIO_NOT_RUNNING
MULTIPLE_SIGMASTUDIO_INSTANCES
SIGMASTUDIO_UNSUPPORTED_VERSION

SIGMASTUDIO_SERVER_DLL_NOT_FOUND
SIGMASTUDIO_SERVER_CONNECTION_FAILED

PROJECT_NOT_OPEN
PROJECT_OPEN_FAILED
PROJECT_SAVE_FAILED
PROJECT_DIRTY
PATH_NOT_ALLOWED

BLOCK_NOT_FOUND
BLOCK_TYPE_UNKNOWN
BLOCK_TYPE_UNSUPPORTED
BLOCK_AUTOMATION_UNVERIFIED
CONTROL_NOT_FOUND
CONTROL_VALUE_OUT_OF_RANGE

CONNECTION_INVALID
PIN_NOT_FOUND

LINK_FAILED
COMPILE_FAILED
DOWNLOAD_FAILED
STATUS_TIMEOUT

CAPTURE_WINDOW_NOT_FOUND
CAPTURE_UNAVAILABLE
CAPTURE_PARSE_FAILED

STALE_REVISION
MUTATION_CONFLICT
BUSY
TIMEOUT

BRIDGE_NOT_RUNNING
BRIDGE_DISCONNECTED
INTERNAL_ERROR
```

---

# 28. Concurrency

Egyetlen fizikai erőforrás van.

Ezért:

```text
global SigmaStudioOperationLock
```

Minden SigmaStudio művelet ezen keresztül fusson.

Nem lehet egyszerre:

```text
deploy + parameter write
graph edit + compile
két block edit
```

A `sigma_status` read-only kérés rövid lockot kaphat.

Ha hosszú művelet fut:

```text
BUSY
```

vagy várjon konfigurált maximum ideig.

---

# 29. Timeoutok

Alapérték:

```text
UI query                 3 s
project open             30 s
project save             30 s
link                     60 s
compile                  60 s
download                 60 s
deploy                  120 s
capture_wait maximum     30 s
```

Ne legyenek indokolatlan:

```text
Thread.Sleep(5000)
```

jellegű megoldások.

Állapotváltozásra poll/event alapú várakozás legyen.

---

# 30. Biztonság

## 30.1 Filesystem

Default:

```json
{
  "allowArbitraryPaths": false
}
```

Konfigurálható projekt rootok:

```text
allowedProjectRoots
```

Minden pathot:

- canonicalizálni kell;
- `..` traversal ellenőrzés;
- UNC path tiltott defaultban;
- symlink/junction root escape ellenőrzés.

## 30.2 Hálózat

MCP kizárólag:

```text
127.0.0.1
```

A szerver semmilyen automatikus remote bindingot ne végezzen.

## 30.3 DSP veszélyes műveletek

Raw register/parameter write alapból disabled.

High-level control írásnál a dokumentált tartományokat validálni kell.

## 30.4 Project destructive operations

Meglévő projekt első módosítása előtt automatikus backup legyen.

Példa:

```text
<ProjectFolder>/.sigmastudio-mcp/backups/
```

A `project_close` ne dobjon el unsaved változtatást implicit módon.

---

# 31. Audit és logging

Használjon strukturált loggingot.

Log könyvtár:

```text
%LOCALAPPDATA%\SigmaStudioMcp\logs\
```

Logolni kell:

```text
operationId
timestamp
tool
duration
project
designRevision
runtimeRevision
SigmaStudio state before/after
result/error
```

Paramétermódosításnál:

```text
block
control
oldValue
newValue
```

Nyers teljes DSP program dumpot ne írjon normál application logba.

---

# 32. Diagnostics

CLI:

```text
sigmastudio-mcp doctor
```

ellenőrizze:

```text
Windows version
.NET version
SigmaStudio install
SigmaStudio version
SStudio.exe process
SigmaStudioServer.dll
Bridge connectivity
UI Automation main window
Status bar selector
Capture Window selector
project state
catalog version
```

További developer parancs:

```text
sigmastudio-mcp ui-dump
```

UI Automation tree mentése JSON-ba.

Ez kulcsfontosságú lesz a Capture Window selectorok fejlesztéséhez.

---

# 33. SigmaStudio és privilege level

A SigmaStudio és Bridge ugyanazon Windows user alatt, azonos integrity levelen fusson.

SigmaStudio administrator módban futtatása nem támogatott default setupként.

Ha SigmaStudio elevated, de Bridge nem:

```text
UI Automation hozzáférés részlegesen blokkolható
```

Ilyenkor a doctor egyértelmű hibát jelezzen.

---

# 34. Capture Window PoC – hard gate

A teljes rendszer fejlesztése előtt Codex készítsen proof-of-conceptot.

PoC kötelezően demonstrálja:

```text
1. SigmaStudio 4.7 process megtalálása
2. status bar kiolvasása
3. Capture Window megnyitása
4. Capture tartalom kiolvasása
5. egy kontroll módosítása
6. új Capture sor felismerése
7. Active: Downloaded állapot felismerése
```

Amíg ez nem működik megbízhatóan, a teljes MCP réteg fejlesztése ne legyen késznek tekintve.

Ha Grid/Table UIA nem működik, implementálni kell a Clipboard fallbackot.

OCR nem elfogadható fallback.

---

# 35. Block control PoC

Második hard gate.

Tesztprojekt:

```text
Input
→ Gain
→ Output
```

Teszt:

```text
project open
→ deploy
→ Gain current value read
→ setControlValue
→ Capture Windowban új write
→ státusz továbbra is Active: Downloaded
→ control value readback
```

Ha ez működik, akkor a REW iterációhoz szükséges gyors runtime tuning architektúra validált.

---

# 36. Graph mutation PoC

Disposable project:

```text
Input
→ Output
```

Automatikusan:

```text
add Gain
disconnect Input → Output
connect Input → Gain
connect Gain → Output
link
compile
download
```

Ezután:

```text
graph_get
```

eredményben a Gainnek és a két kapcsolatnak meg kell jelennie.

Majd:

```text
save
close
reopen
graph_get
```

ugyanazt a struktúrát kell adnia.

---

# 37. Tesztstratégia

## 37.1 Unit tests

Hardware nélkül fussanak.

Kötelező:

```text
Bridge protocol framing
Bridge request validation
path canonicalization
revision handling
mutation idempotency
state machine
Graph XML parser
Netlist parser
.params parser
Catalog parser
ADAU1701 compatibility normalization
control range validation
Capture row parser
status text normalization
script string escaping
error mapping
```

Minimum code coverage cél:

```text
Core/Graph/Catalog: 80%
```

Coverage önmagában nem acceptance criterion, csak minimum ellenőrzés.

## 37.2 Golden/snapshot fixtures

Valós SigmaStudio exportokból sanitizált fixture-ek:

```text
simple_gain/
peq/
mixer/
nested_hierarchy/
invalid_unconnected/
```

Parser változtatásnál regression teszt.

## 37.3 Bridge contract tests

Mock SigmaStudio adapterrel:

```text
Open → Save
Link → Compile → Download
failed compile
timeout
Capture unavailable
stale revision
```

## 37.4 UI Automation fixture

Készüljön egy kis saját WPF/WinForms fixture alkalmazás, amely reprodukál:

```text
StatusBar
Capture-like grid
tabs
context menu
clipboard copy
```

Így a UIA core logika CI-ben tesztelhető SigmaStudio nélkül.

## 37.5 SigmaStudio integration tests

Valós SigmaStudio 4.7, de DSP nélkül is futtatható tesztek:

```text
server attach
new project
open/save
script execution
insert/remove block
export
graph parse
```

A hardware-t ténylegesen igénylő teszteket külön category jelölje.

## 37.6 Hardware-in-the-loop

Környezet:

```text
SigmaStudio 4.7
USBi
ADAU1701
```

Kötelező HIL tesztek:

```text
project download
Active: Downloaded detection

100 egymást követő realtime control change
100 Capture delta felismerés

block add
connection change
link/compile/download

project save/reopen

parameter read/write smoke test

20 egymást követő deploy ciklus
```

Nem lehet:

```text
deadlock
Bridge crash
SigmaStudio automation exception
dupla mutation
téves readyForMeasurement
```

---

# 38. REW end-to-end acceptance

Nem automatikus repository unit test, hanem system acceptance test.

Agent rendelkezik:

```text
SigmaStudio MCP
REW API/MCP
```

Scenario:

```text
1. DSP project open
2. ready_for_measurement
3. REW baseline measurement
4. DSP gain/filter módosítás
5. Capture verification
6. ready_for_measurement
7. REW new measurement
8. további paramétermódosítás
9. új mérés
10. project save
```

A teljes folyamat során a felhasználónak nem kell SigmaStudio-ban kattintania.

Ez a projekt végső üzleti acceptance tesztje.

---

# 39. Performance célok

Nem hard real-time rendszer, de célértékek:

```text
sigma_status                 < 500 ms
block control update         < 1 s
Capture delta láthatóság     < 500 ms tipikusan
MCP saját overhead           < 100 ms
```

Compile/download idő nem tartozik ezekbe, mert SigmaStudio/hardware függő.

---

# 40. Dokumentációs követelmények

A fejlesztés akkor sem kész, ha a kód működik, de nincs dokumentálva.

Kötelező dokumentumok:

```text
README.md
docs/architecture.md
docs/mcp-tools.md
docs/bridge-protocol.md
docs/graph-model.md
docs/block-catalog.md
docs/ui-automation.md
docs/testing.md
docs/hardware-test-procedure.md
docs/troubleshooting.md
docs/source-register.md
CHANGELOG.md
```

## Kötelező ADR-ek

```text
ADR-0001 two-process architecture
ADR-0002 SigmaStudioServer + IScripted strategy
ADR-0003 UI Automation only for observation
ADR-0004 no dspproj reverse engineering
ADR-0005 ADI Wiki block catalog
ADR-0006 localhost MCP transport
ADR-0007 raw parameter/register access policy
```

---

# 41. Source register

`docs/source-register.md` minden külső technikai forrást tartalmazzon:

```text
title
publisher
retrieved date
what information is derived from it
status: authoritative/supporting
```

Kiemelten kötelező források:

**Analog Devices – SigmaStudioServer**  
Az automation server, annak same-PC követelménye és API-k. citeturn119067view0

**Analog Devices – SigmaStudio Scripting**  
IScripted és külső automation kapcsolat. citeturn999333search1

**Analog Devices – Creating and Running SigmaStudio Scripts**  
Object insertion, connection, property és project scripting példák. citeturn659050search0

**Analog Devices – Sample Scripts for IScripted Interface**  
`setControlValue` viselkedés. citeturn659050search6

**Analog Devices – Viewing Control Parameter Names**  
Control parameter nevek meghatározása. citeturn226673search0

**Analog Devices – SigmaStudio Toolbox**  
Blokkok és algoritmusok hivatalos katalógusa. citeturn536698search0

**Analog Devices – Capture Output Data**  
Capture Window jelentése és mezői. citeturn378160search0

**Analog Devices – Link/Compile/Download**  
Compile/download és `Active: Downloaded` state. citeturn786035search0

**Analog Devices – Export Program and Parameters**  
Exportált parameter adatok. citeturn516936search0

**Analog Devices – Import SigmaStudio Projects**  
SigmaStudio 4.6+ XML/Netlist export rekonstruálhatósága. citeturn516936search7

**Analog Devices EngineerZone – Capture Window Output Recording**  
A klasszikus Capture Window közvetlen API hiánya. citeturn653043search0

**Microsoft – UI Automation**  
A GUI observer technikai alapja. citeturn713082search15turn713082search7

**Model Context Protocol – 2026-07-28**  
MCP protokoll referencia. citeturn568866search0

**Official MCP C# SDK**  
MCP C# implementáció referencia. citeturn279317search2

---

# 42. Fejlesztési fázisok

## Phase 0 – Research/Probe

Eredmény:

```text
SigmaStudio version discovery
DLL load
server ping
UI tree dump
status read
Capture read
setControlValue smoke test
```

Output dokumentáció:

```text
docs/research/sigmastudio-4.7-runtime-findings.md
ui-profiles/sigmastudio-4.7-en-US.json
```

Phase csak akkor PASS, ha Capture + status olvasható.

## Phase 1 – Bridge

Implementálni:

```text
Named Pipe
STA dispatcher
late bound SigmaStudioServer
project operations
build/download
error handling
```

Tests PASS.

## Phase 2 – UI Observer

Implementálni:

```text
status observer
Capture Window discovery
Capture reader
clipboard fallback
cursor/delta
```

Valós SigmaStudio integration PASS.

## Phase 3 – Export/Graph

Implementálni:

```text
Export System Files
XML parser
Netlist parser
params parser
normalized graph
```

Fixture tesztek PASS.

## Phase 4 – Block Catalog

Implementálni:

```text
CatalogBuilder
ADI Wiki parsing
ADAU1701 filtering
source provenance
offline JSON catalog
```

Catalog validation PASS.

## Phase 5 – Schematic mutation

Implementálni:

```text
block add
block remove
rename
controls
connections
undo/redo
```

Graph mutation HIL PoC PASS.

## Phase 6 – MCP

Implementálni a teljes publikus tool/resource készletet.

Contract tests PASS.

## Phase 7 – Reliability

Implementálni:

```text
revision protection
mutation idempotency
backup
audit log
timeouts
recovery
doctor
```

Soak tests PASS.

## Phase 8 – Final HIL + REW workflow

20 deploy ciklus és agent + REW end-to-end teszt.

---

# 43. Codex fejlesztési szabályok

Codex a projekt implementálásakor:

1. Ne találjon ki undocumented SigmaStudio neveket vagy API-kat.
2. Előbb reprodukálható probe-ot írjon, utána production implementációt.
3. Minden SigmaStudio-verziófüggő megállapítást dokumentáljon.
4. Minden bugfixhez regression test tartozzon.
5. ADI binaryt ne commitoljon.
6. `.dspproj` fájlt közvetlenül ne szerkesszen.
7. Productionban ne használjon OCR-t/pixelkattintást.
8. Minden commit előtt build és releváns test suite fusson.
9. Hardware nélkül futó teszt soha ne igényelje SigmaStudio telepítését.
10. A HIL teszteket külön category alatt tartsa.
11. Sikertelen részleges DSP/schematic műveletet ne jelentsen sikeresnek.
12. Dokumentációt ugyanabban a változtatásban frissítse, mint az API-t.
13. Ha a runtime viselkedés eltér ettől a tervtől, ne kerülje meg csendben: készítsen ADR-t az eltérésről.

Build gate:

```text
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Ezek hibamentes lefutása nélkül feladat nem tekinthető késznek.

---

# 44. Definition of Done

A projekt **csak akkor tekinthető v1 késznek**, ha az alábbiak mind teljesülnek.

### Build

Tiszta repository clone után Windows build sikeres úgy, hogy az ADI DLL nincs repositoryban.

### SigmaStudio connection

A rendszer automatikusan megtalálja vagy konfigurációból megtalálja SigmaStudio 4.7-et, és stabilan kapcsolódik hozzá.

### Projekt

Agent:

```text
create
open
save
save-as
close
checkpoint
```

műveleteket végre tud hajtani.

### Graph

Egy meglévő ADAU1701 projektből az agent megkapja:

```text
blocks
algorithms
pins
controls/parameters
connections
sample rate
```

adatait normalizált formában.

### Blocks

Agent képes:

```text
search docs
read docs
add
remove
rename
read control
set control
```

műveletekre legalább az ADAU1701-en ellenőrzött blokkok esetén.

### Connections

Agent létre tud hozni és törölni kapcsolatokat pin index vagy egyértelmű pin név alapján.

### Compile/download

Agent önállóan:

```text
link
compile
download
deploy
```

műveletet végez.

### Status

A szerver megbízhatóan felismeri:

```text
Design Mode
Ready: Compiled
Active: Downloaded
```

állapotokat.

### Capture

Egy ismert runtime control módosítás után az MCP-n keresztül kiolvasható az ehhez tartozó új Capture Window adat.

### Measurement readiness

A:

```text
sigma_ready_for_measurement
```

nem adhat `true` értéket deployolatlan design mellett.

### Reliability

Teljesül:

```text
100 egymást követő realtime control update
20 egymást követő deploy
```

agent/manual intervention nélkül.

### Persistence

Blokk hozzáadás + kapcsolat módosítás:

```text
save
→ close
→ reopen
```

után változatlanul jelen van.

### Safety

Raw register/parameter írás defaultban tiltva.

### Documentation

Minden kötelező docs és ADR elkészült.

### End-to-end

Egy agent képes:

```text
SigmaStudio paraméter állítás
→ státusz/Capture ellenőrzés
→ REW mérés
→ új SigmaStudio állítás
→ új mérés
```

ciklust legalább öt egymást követő iterációban végrehajtani anélkül, hogy a felhasználónak a SigmaStudio GUI-hoz hozzá kellene nyúlnia.

---

# 45. Ismert technikai kockázatok

## Capture Window UI Automation

Ez a projekt legnagyobb verziófüggő kockázata.

Mitigation:

```text
UIA Grid/Table
→ Text/accessibility
→ clipboard fallback
```

Ezért Phase 0 hard gate.

## SigmaStudio undocumented runtime részletek

A külső Server API dokumentált, de egyes blokknevek és konkrét algoritmusvariánsok runtime discoveryt igényelhetnek.

Mitigation:

```text
ADI Wiki
+ installed toolbox
+ disposable insert verification
```

## Manual user changes

Ha a user az MCP mellett kézzel módosítja a schematicot, a Host cached state elavulhat.

Mitigation:

```text
status observer
revision model
graph refresh
external-dirty detection
```

## SigmaStudio crash

A Bridge ne feltételezze, hogy a SigmaStudio örökké fut.

Ha eltűnik:

```text
state → NO_APPLICATION
current project cache invalid
mutations disabled
```

A Host maradjon futóképes.

---

# 46. Tervezési alapelv

A rendszernek nem „SigmaStudio távirányítónak”, hanem **strukturált DSP control plane-nek** kell lennie.

Az agent ne ezt lássa:

```text
click menu
move slider
press F7
```

hanem ezt:

```text
set block control
connect blocks
deploy design
verify DSP state
read communication trace
ready for measurement
```

A SigmaStudio GUI kizárólag ott figyelendő UI Automationnel, ahol a hivatalos API nem ad hozzáférést: elsősorban a Capture Window és a státusz visszajelzés esetén.

Minden más művelethez a hivatalos SigmaStudioServer/IScripted felületet kell használni.