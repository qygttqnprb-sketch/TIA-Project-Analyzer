# Esiti verificati · 4 novembre 2026

## Aggiornamento V0.7

- 141 controlli automatici .NET 10 superati con dati sintetici/simulati.
- 15 verifiche WPF/net48 superate, zero errori di binding: cache fredda/calda,
  traccia dei consensi, ZIP mirato e invalidazione dei risultati precedenti.
- Build Debug e Release x64 contro Base/Step7 V21 e build fallback senza Siemens:
  riuscite senza errori/avvisi. Nessuna DLL Siemens redistribuita.
- Nella demo, il riuso ripristina cinque XML e sei analisi; la tabella tag è sempre
  riesportata. Il pacchetto MotorRun include quattro oggetti su sei, con inventario
  ed esclusioni dichiarati. Non sono misure di accelerazione su un progetto reale.
- Verificati cache corrotte, impronte instabili, IF/ELSIF/ELSE e guardie del chiamante,
  conservazione delle stringhe, reti non modellate, rinumerazione UID LAD, contatti
  negati, XML alterati, cancellazione del confronto e pacchetti con selezione diversa.
- I servizi Siemens CrossReference/Fingerprint sono compilati contro le API installate;
  raccolta dei loro risultati e riuso su progetti PLC reali restano da validare.

## Baseline V0.5

Windows x64, .NET SDK 10.0.403. DLL installate Base e Step7: identità assembly
`21.0.0.0`, nel percorso standard PublicAPI V21/net48.

## Risultati

- Build Release e Debug x64 con `EnableSiemensOpenness=true`: riuscite, zero errori/avvisi.
- Build Debug con adapter Siemens escluso: riuscita, zero errori/avvisi.
- `TiaAnalyzer.Checks`: 98 controlli superati, backend sintetici/simulati su .NET 10.
- `TiaAnalyzer.WpfChecks`: 11 verifiche superate su WPF/net48, senza errori di binding.
  Rilevamento/selezione demo, collegamento, sei componenti, ZIP, ricerca, confronto,
  disconnessione e chiusura verificati. Immagini controllate a 1180 e 900 pixel.
- ZIP demo creato con sei XML, documentazione, indici, manifest e grafo.
- Pacchetto applicativo Release creato con eseguibile, config e tre DLL applicative;
  nessuna DLL Siemens redistribuita.

## Stato reale aggiornato

Il controllo sul token Windows corrente restituisce **OK** per il gruppo locale
`Siemens TIA Openness`. Questo aggiorna il precedente sospetto di gruppo assente:
non descrive più lo stato rilevato. Rimane separato il consenso Siemens di Attach.

L'adapter Release reale ha eseguito `TiaPortal.GetProcesses()` senza errore e ha
rilevato **zero istanze TIA aperte**. Non è stato eseguito Attach e non è stato
aperto o letto alcun progetto PLC reale. Per completare la verifica occorre aprire
manualmente un progetto di prova autorizzato, selezionare il PID e collegarsi
dalla dashboard. Seguire [WINDOWS-VALIDATION.md](WINDOWS-VALIDATION.md).

## Limiti residui

Export reale, contenuti specifici V21, DPI 125%/150% e comportamento su un impianto
non sono certificati dai test sintetici. HMI, allarmi, parametri azionamenti e
analisi semantica completa delle sequenze restano fuori dalla V0.5.
La V0.5 prepara evidenze verificabili per l'analisi: non formula una diagnosi runtime.
