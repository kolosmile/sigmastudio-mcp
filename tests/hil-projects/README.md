# Disposable SigmaStudio HIL projects

Mutation HIL is intentionally separate from the read-only `SIGMASTUDIO_MCP_HIL=1` gate.

Provide a real SigmaStudio 4.7 project through:

    $env:SIGMASTUDIO_MCP_HIL = "1"
    $env:SIGMASTUDIO_MCP_HIL_MUTATION = "1"
    $env:SIGMASTUDIO_MCP_HIL_PROJECT = "C:\path\to\control-write-test.hil.dspproj"

The project must be opened in SigmaStudio before the mutation test starts. The Bridge
accepts only the exact configured path, a filename ending in `.hil.dspproj`, or a
project below this `tests/hil-projects` directory. Do not point the mutation gate at
an ordinary user project.

The first required fixture is a small ADAU1701 Input → scalar control block → Output
design. The test block and control are selected with:

    $env:SIGMASTUDIO_MCP_HIL_OBJECT = "Gain1"
    $env:SIGMASTUDIO_MCP_HIL_CONTROL = "Gain"
    $env:SIGMASTUDIO_MCP_HIL_NEW_VALUE = "0.25"

The `.dspproj` itself is not generated or edited by this repository.
