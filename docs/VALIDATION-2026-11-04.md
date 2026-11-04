# Esiti verificati · 4 novembre 2026

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
