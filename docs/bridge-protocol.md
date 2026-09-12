# Bridge protocol

Named Pipe neve: `SigmaStudioMcp.<CurrentUserSidHash>`.

Az üzenet UTF-8 JSON, 32 bites little-endian length prefixszel, maximum 16 MiB mérettel:

```json
{"id":"uuid","method":"project.open","params":{"path":"..."},"timeoutMs":30000}
```

```json
{"id":"uuid","ok":true,"result":{},"error":null,"durationMs":12}
```

Kezdő handshake: `bridge.get_version`, `bridge.get_capabilities`, `bridge.ping`. A Bridge pipe ACL-je kizárólag az aktuális Windows user SID-jének engedélyez olvasást/írást.
