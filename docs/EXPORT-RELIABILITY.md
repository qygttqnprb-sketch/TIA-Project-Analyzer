# V1.0 · affidabilità e diagnosi dell'esportazione

## Incidente e conclusioni consentite

L'utente ha segnalato circa 12 ore di attesa dopo `48 oggetti — FC5_Robot_Head_Shell — Exported`. Non sono disponibili dump, log prima/dopo le chiamate o misure del progetto reale. **La causa non è identificata e FC5 non è dichiarato responsabile.**

L'ispezione della V0.9 (`3915043`) mostra:

| Evidenza nel codice | Conseguenza possibile, non causa provata |
| --- | --- |
| `PlcExportWriter.Write` pubblica il progresso prima di `ReadReferences` | FC5 può essere l'ultimo XML completato mentre la chiamata successiva è ancora relativa ai suoi riferimenti |
| `SiemensEvidence.V21` invoca `GetCrossReferences(AllObjects)` per ogni oggetto | Costo ripetuto potenzialmente elevato; i limiti sulla visita dei risultati non limitano la durata della chiamata stessa |
| Fingerprint prima/dopo l'export e iteratori Siemens senza progressi intermedi | Anche metadata, GetService, MoveNext o fingerprint possono essere il punto di attesa |
| `SerialWorker` ha un solo STA e nessuna preemption | Una chiamata sincrona non ritornante trattiene anche le richieste di disconnessione in coda |
| UI in await e nessun heartbeat di export indipendente | L'ultimo messaggio visibile non identifica l'operazione corrente né dimostra un deadlock |
| XML scritti direttamente al percorso finale e manifest solo alla fine | Dopo interruzione non c'era un indice affidabile degli XML completati |
| Cache V0.9 con percorso del progetto e token | Un percorso non dimostra da solo l'identità del progetto né la copertura dell'intero XML da parte del fingerprint |
| Hash del manifest calcolati prima della copia ZIP | Una modifica locale successiva poteva rendere incoerenti i byte copiati nell'archivio |

Non è emerso un ciclo di attesa UI/STA dimostrato. La V1.0 aggiunge un rifiuto esplicito delle richieste accodate dal worker a se stesso; non modifica il modello di threading Siemens senza una prova Windows. Non viene introdotta alcuna chiamata Openness parallela.

## Diagnostica persistente

Ogni nuova esportazione ha un ID e una cartella nuova. `operations.jsonl` è leggibile anche durante l'attesa. Ogni record contiene ID export/operazione, sequenza, nome/tipo/percorso, operazione, fase, inizio/fine UTC, durata monotona, esito, dettaglio errore e ultimo XML completato.

Prima di entrare in ogni operazione strumentata viene scritto e forzato su disco il record `Start`. Sono distinti getter, creazione/avanzamento/lettura/rilascio degli iteratori, GetService, fingerprint, Export XML e cross-reference. Sono registrate anche verifica/ripristino cache, parsing, analisi, report e ZIP. I nomi di gruppi/collection sono registrati prima che sia noto il nome dell'elemento; il percorso disponibile rimane nel record. Nessun oggetto Siemens lascia il worker.

Il timer watchdog legge solo metadati locali e Stopwatch. Produce un avviso una volta per soglia **30, 120 e 300 secondi per operazione**. Non chiama Siemens, non annulla il thread e non chiude TIA. La UI legge uno stato in memoria ogni secondo: non aspetta un callback di progresso Siemens. Il lock dello stato UI non viene mantenuto durante il flush del log; gli avvisi vengono resi visibili anche prima di un'eventuale attesa dell'I/O diagnostico.

Se il log non è scrivibile, non viene iniziata una nuova chiamata strumentata. Gli scope annidati mantengono la fase più interna visibile. La durata include anche la preparazione diagnostica: un avviso non prova da solo che Siemens sia la causa. In caso di arresto del processo, una riga finale JSONL troncata non è un record completo; il precedente `Start` privo di fine identifica il punto da investigare.

## XML atomici e checkpoint

Siemens esporta su un file `.xml` dentro una directory temporanea univoca terminante in `.partial` (per conservare l’estensione XML richiesta dagli exporter). Il file viene accettato soltanto dopo ritorno dell'API, controllo dell'annullamento, parsing XML sicuro (DTD/entità esterne vietate), SHA256 e flush. Soltanto allora viene rinominato atomicamente al percorso XML finale.

Ogni XML valido ha un record atomico in `checkpoint-entries/<hash-id>.json`. `checkpoint.json` contiene lo stato e l'ID. Il costo di aggiornamento per oggetto non riscrive l'intero inventario; i record sono indicizzati in memoria per ID durante la ripresa. Un arresto tra la pubblicazione XML e il record lascia un orfano non riutilizzabile automaticamente. I `.partial` non sono mai accettati come XML completati.

La UI mostra il conteggio degli XML validi e il totale selezionato dopo l'enumerazione. Prima del termine dell'enumerazione il totale è `?`. Eventuali errori di enumerazione rendono esplicitamente incompleta la copertura, anche quando tutti gli oggetti scoperti sono stati tentati.

## Ripresa conservativa

Indicare la cartella precedente nel campo di ripresa. La destinazione è sempre nuova: la sorgente non viene modificata e non si tenta di saltare alla posizione numerica 48.

Per ogni oggetto riscoperto servono tutti i controlli:

- Identità/percorso della sorgente e prova del progetto corrispondenti.
- Identità API, identità oggetto, linguaggio e protezione corrispondenti.
- Fingerprint dell'oggetto presente, stabile prima/dopo e attestato affidabile per l'intero XML.
- Stato Exported/InterfaceOnly, percorso sicuro senza link/junction, file presente, dimensione e SHA256 corretti; verifica dei byte copiati e parsing del file.

Se una prova non è disponibile, non coincide, è incompleta o cambia durante il riuso, si riesporta. I tag senza fingerprint vengono riesportati. Il contenuto della cache è sempre ricopiato nello snapshot; nessun pacchetto dipende da un blob esterno. La cache XML passa a `xml-v2` per non accettare implicitamente le vecchie assunzioni `xml-v1`.

**Nel reale V21 la semplice presenza di fingerprint non attesta la copertura dell'intero XML.** I valori vengono raccolti per diagnosi, ma l'adapter li marca conservativamente non affidabili per il riuso finché non sia stata verificata tale copertura con documentazione e prove Windows. In assenza della prova del progetto e dell'oggetto, sia ripresa sia cache riesportano. La demo e i fixture controllati possono attestare l'esatto contenuto sintetico. Non è dichiarato un aumento di prestazioni reale ottenuto con il riuso.

## Stati e pacchetti

- `Incomplete`: avvio, interruzione anomala o errori di raccolta; non certifica una raccolta completa.
- `Cancelled`: annullamento osservato fra due operazioni; XML/record già completati restano utilizzabili solo dopo le verifiche di ripresa.
- `Collected`: enumerazione e raccolta completa terminate, prima della pubblicazione del manifest verificato.
- `Complete`: raccolta Full terminata e manifest pubblicato dopo verifica degli hash; restano le limitazioni di protezione e di snapshot non transazionale.
- `Partial`: Rapida, Mirata o cross-reference parziali; non implica completezza globale.

L'annullamento non interrompe una chiamata Siemens già entrata: UI e watchdog continuano, ma occorre attenderne il ritorno. Non vengono usati Thread.Abort, terminazioni di processo o disconnessioni concorrenti.

Un archivio annullato/in corso non viene pubblicato. Un pacchetto con errori noti conserva `Incomplete` e gli esiti individuali nel manifest/report: non viene chiamato completo. Il manifest descrive la raccolta XML; da solo non certifica l'esistenza di uno ZIP. Lo ZIP viene rinominato soltanto dopo chiusura, flush e ultimo controllo di annullamento, verificando anche gli hash degli XML effettivamente copiati. Un errore di packaging può lasciare XML e manifest consultabili, ma nessun nuovo percorso ZIP viene presentato come successo.

## Modalità

| Modalità UI/API | Raccolta | Copertura dichiarata |
| --- | --- | --- |
| Quick / Rapida | Inventario e tutti gli XML accessibili; niente fingerprint, cross-reference o analisi logica opzionale | Partial; l'assenza di report logici non prova assenza di dipendenze |
| Full / Completa | Tutti gli oggetti scoperti, fingerprint diagnostici, riferimenti consentiti e analisi locale | Complete solo dopo raccolta/manifest riusciti, altrimenti Incomplete/Partial |
| Targeted / Mirata | Inventario completo, XML solo dei nomi/ID esatti separati da `;` | Partial; ID inclusi nel manifest, esclusi in Selection.ExcludedIds e inventario completo in export-inventory.json |

Un nome condiviso da più scope include tutti gli oggetti con quel nome; per precisione usare gli ID. Un selettore inesistente fa fallire prima degli export. Non si aggiungono dipendenze implicite nella nuova modalità Mirata.

Il filtro semantico dei pacchetti V0.9 è distinto: funziona dopo una raccolta Full e conserva la selezione conservativa precedente. Non può essere applicato sopra una raccolta già Mirata o Rapida. Schema manifest `1.0` e campi precedenti restano; i nuovi metadati sono opzionali. I vecchi ZIP/manifest V0.9 continuano a essere leggibili.

## Misure e prestazioni

`metrics.json` e JSONL distinguono Enumeration, Fingerprint, XmlExport, CrossReferences, XmlParsing, LogicAnalysis, ReportGeneration e ZipCompression, più LocalCache. Le fasi omesse sono in SkippedPhases; zero millisecondi non significa che una chiamata Siemens sia stata provata o sia gratuita. Parsing comprende validazione degli XML e parsing per analisi. Le misure non includono tutte le spese generali di flush/queue nello stesso modo: non sommarle per dedurre il tempo totale. ExportMilliseconds e il tempo complessivo UI restano separati. La durata finale della compressione è nel log e nel metrics.json esterno; lo ZIP contiene la misura payload disponibile prima di chiudere l'archivio.

Sono rimossi il secondo attraversamento hardware nell'export e le query AllObjects successive all'esaurimento del budget globale di riferimenti. Non si memorizza un risultato AllObjects come se fosse riutilizzabile per altri oggetti senza verificarne lo scope. Nessuna fase Siemens è parallelizzata. Parsing e grafo condividono stato/risoluzione: restano sequenziali per evitare picchi di memoria e alterazioni semantiche senza misure rappresentative; nessun task locale legge proxy Siemens.

Benchmark ripetibile, esclusivamente sintetico:

```powershell
dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj -- --export-benchmark artifacts/benchmark-nuovo
```

Usare una destinazione nuova. Genera tre raccolte/ZIP, log, metriche e summary.tsv. I valori demo non stimano la durata del progetto reale. Per il progetto segnalato occorre raccogliere un nuovo log su progetto di prova e confrontare Rapida/Completa con inventario e modifiche controllate.
