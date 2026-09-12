# SigmaStudio 4.7 runtime findings

This file is a probe template, not a claim that the local machine has passed the Phase 0 gate.

Required evidence before enabling the production Bridge:

- installed SigmaStudio version and executable path;
- `Analog.SigmaStudioServer.dll` path and discovered public type/method surface;
- successful `bridge.ping` and capability handshake;
- status bar selector and observed values;
- Capture Window selector, row extraction and clipboard fallback;
- verified `setControlValue` write/readback on a disposable ADAU1701 project.

Until those observations are recorded, the adapter keeps runtime-specific object names unverified.
