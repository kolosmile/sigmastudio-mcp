# ADR-0008: net8 build compatibility

Status: accepted

A specifikáció .NET 10-et céloz. A fejlesztői környezetben a .NET 8 SDK az elérhető compiler, miközben .NET 10 runtime telepítve van, ezért a kezdeti repository `net8.0`/`net8.0-windows` targetekkel készült. A kód nem használ .NET 10-only API-t; .NET 10 SDK-ra váltáskor a target frameworkek és `global.json` frissíthetők.
