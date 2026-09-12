# Block catalog

A katalógus az Analog Devices hivatalos Toolbox Wiki szemantikai metadata-rétegét reprezentálja. Teljes Wiki-mirror nem kerül a repositoryba. Minden rekord tartalmaz kompatibilitást, control/pin metadata-t, automation mezőket és source provenance-t.

`unknown` kompatibilitás nem emelhető automatikusan `supported` értékre. Az installed SigmaStudio runtime capability source-of-truth a konkrét automation névre és elérhetőségre; az `automation.insert=runtime-discovery-required` rekordok csak probe után tekinthetők verifiednek.
