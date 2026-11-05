# Esiti V1.0 · 5 novembre 2026

## Verifiche eseguite nel cloud Linux

- SDK .NET 10.0.403.
- **265 controlli automatici superati** su .NET 10 con dati sintetici/backend simulati, inclusa la suite V0.9 aggiornata ai nuovi vincoli di integrità.
- Build soluzione Debug e Release net48/x64 con `EnableSiemensOpenness=false`: riuscite, zero warning/errori.
- Build del runner WPF/net48, incluso `--reliability-only`: riuscita, zero warning/errori. **Il runner non è stato eseguito su Windows in questa sessione.**
- Parsing sintattico Roslyn C# 7.3 dei file `*.V21.cs` con simbolo `SIEMENS_V21` attivo: zero errori di sintassi. Non è una compilazione contro le API e non usa stub Siemens.
- Nessuna DLL Siemens redistribuita, nessuna connessione a TIA/PLC e nessuna modifica a progetti Siemens.

Copertura nuova: Start persistito prima dell'export simulato; staging e checkpoint atomici; watchdog 30/120/300 con soglie iniettate per test deterministici e timer indipendente; operazione simulata bloccata con heartbeat reale; cancellazione differita e conservazione degli XML completati; ripresa con prove/hash corretti; rifiuto/riacquisizione per identità, API, linguaggio, protezione, fingerprint non affidabili/cambiati, file assenti/alterati/parziali; XML/DTD non validi; stati Partial/Incomplete/Cancelled/Complete; budget globale riferimenti; limite di auto-accodamento del worker; selezione esplicita e inventario/esclusioni; compatibilità con manifest senza campi V1.0; hash dei byte copiati nello ZIP.

Le soglie watchdog sono verificate senza attendere cinque minuti; questo non è un test di una chiamata Siemens realmente bloccata per 300 secondi. Il test di attesa simulata misura anche l'avanzamento del clock e del monitor MVVM durante il blocco, poi rilascia volontariamente la fixture; nessun thread è abortito.

## Misura sintetica di esempio

Comando eseguito:

```text
dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj -- --export-benchmark artifacts/v1.0-benchmark-final
```

Valori di una singola esecuzione, millisecondi; sei XML demo, un XML nella Mirata:

| Fase | Completa (6) | Rapida (6) | Mirata (1) |
| --- | ---: | ---: | ---: |
| Enumerazione | 16 | 6 | 3 |
| Fingerprint sintetici | 3 | omessa | 0 |
| Export XML sintetico | 5 | 3 | 0 |
| Cross-reference sintetici | 1 | omessa | 0 |
| Parsing/validazione XML | 19 | 0 | 1 |
| Analisi logica locale | 109 | omessa | 1 |
| Report | 101 | 7 | 8 |
| Compressione ZIP | 29 | 5 | 6 |

Zero significa sotto la risoluzione del millisecondo in questa esecuzione, non assenza garantita di lavoro. Il primo passaggio include costi di inizializzazione/JIT, i successivi sono caldi; il flush diagnostico influisce sui tempi. **Non è un confronto statistico né una misura o promessa di accelerazione Siemens.** La demo non replica dimensione, protezioni, topologia o servizi del progetto rimasto in attesa per 12 ore.

Log, XML, ZIP e metriche di questa prova sono generati nella cartella artifacts ignorata da Git. Il comando permette di rigenerarli in una destinazione nuova.

## Verifiche ancora necessarie

- Compilazione dei file Siemens V1.0 condizionali contro le DLL Base/Step7 V21 installate su Windows. Le precedenti build Siemens V0.9 non certificano queste modifiche.
- Esecuzione WPF/net48 e test di binding dei nuovi controlli/heartbeat, compresi DPI e layout minimo.
- Export verso la directory temporanea `.partial` con file avente estensione `.xml`, enumerazione in due fasi, fingerprint e riferimenti reali.
- Nuova raccolta su progetto di prova immutato e confronto Rapida/Completa/Mirata con log prima/dopo le chiamate.
- Attestazione documentata della copertura dei fingerprint sull'intero XML prima di qualsiasi abilitazione del riuso reale. Nel frattempo la ripresa reale riesporta gli oggetti non dimostrabili.
- Identificazione della causa dell'attesa reale: **non determinata** dai test sintetici o dalla sola analisi del codice.

Seguire [WINDOWS-V1.0-CHECKLIST.md](WINDOWS-V1.0-CHECKLIST.md) e conservare l'intera cartella diagnostica della prova. Il log è un file locale di progetto/operazioni, non viene inviato automaticamente.
