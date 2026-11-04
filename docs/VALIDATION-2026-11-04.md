# Esiti verificati · 4 novembre 2026

## Aggiornamento V0.9

- 225 controlli automatici .NET 10 superati.
- 30 controlli offline e 51 controlli grafici ripetuti su net48.
- 21 verifiche WPF superate, zero errori di binding: ZIP aperto da disconnesso,
  scelta della rete, anteprima XML, condizioni e siti di scrittura.
- Build Debug/Release x64 con Siemens V21 riuscite senza errori/avvisi.
- Verificati hash errati, XML/manifest duplicati, file mancanti, report serializzati
  ignorati, percorsi non sicuri, compatibilità dei campi opzionali, selezioni mirate,
  DTD, cancellazione, anteprime troncate e modifiche dello snapshot dopo l'apertura.
- Il contesto precedente resta disponibile quando l'apertura fallisce.
  La ricerca offline funziona anche con diagnostica Siemens indisponibile simulata.
- Nessun archivio viene estratto o modificato durante apertura/anteprima.
  Prove e ZIP usano dati sintetici; Attach/export e reti di progetti reali restano
  da validare. Dettagli in [OFFLINE-SNAPSHOTS.md](OFFLINE-SNAPSHOTS.md).

## Aggiornamento V0.8

- 195 controlli automatici .NET 10 con dati sintetici/simulati.
- 51 controlli di reti grafiche e scritture ripetuti su .NET Framework 4.8:
  tabelle di verità, rami paralleli, negazioni, Set/Reset, ambiti, cache,
  XML malformati e uscite di chiamata dichiarate ma non modellate.
- 18 verifiche WPF/net48 superate, zero errori di binding: ricerca dei siti,
  invalidazione dei risultati, report, ZIP completi/mirati e confronto.
  Verificate le schermate a 1180 e 900 pixel.
- Build Debug e Release x64 contro le DLL Siemens V21 installate: riuscite
  senza errori/avvisi. Nessuna DLL Siemens redistribuita.
- Il pacchetto completo demo include writers.json/md e 29 file dichiarati.
  La demo MotorRun conserva due rami esclusivi nello stesso blocco:
  MultipleSites, senza dichiarare conflitto o ordine dell'ultima scrittura.
- L'analisi LAD/FBD riguarda il sottoinsieme FlgNet v4 documentato in
  [GRAPH-WRITERS.md](GRAPH-WRITERS.md). Le prove usano esclusivamente dati
  sintetici; il contenuto reale di un progetto V21 resta da validare.

I risultati precedenti sotto sono conservati come storico, non aggiunti ai
conteggi della suite attuale.

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
