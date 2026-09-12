# Troubleshooting

## `BRIDGE_NOT_RUNNING`

Indítsd a `SigmaStudio.Bridge` processzt ugyanazon Windows user alatt, majd ellenőrizd a pipe nevét.

## `SIGMASTUDIO_SERVER_DLL_NOT_FOUND`

Add meg a telepített `Analog.SigmaStudioServer.dll` abszolút útját. A DLL-t ne másold a repositoryba.

## `PROJECT_DIRTY`

A close művelet szándékosan nem dob el implicit módon mentetlen változást. Ments vagy készíts checkpointot.

## `STALE_REVISION`

Olvasd újra a `sigma_status` eredményét és küldd újra a mutationt az aktuális `designRevision` értékkel, új `mutationId`-val.
