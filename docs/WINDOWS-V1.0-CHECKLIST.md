# Checklist Windows · V1.0

Le prove cloud sono sintetiche. Non identificano la causa del blocco reale e non certificano l'adapter Siemens modificato. Usare inizialmente un progetto di prova offline, aperto manualmente e autorizzato; nessun impianto/PLC online.

## Compilazione e regressioni

1. Compilare Debug/Release x64 contro le DLL V21 locali con `EnableSiemensOpenness=true`; controllare tutte le nuove lambda/collezioni nel file condizionale. Nessuna DLL va copiata nel repository.
2. Eseguire `dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj` per test simulati.
3. Compilare `TiaAnalyzer.WpfChecks` con lo stesso flag, avviare il runner normale per binding/demo e ripetere `--offline-only`, `--graph-only`.
4. Eseguire il runner con `--reliability-only`: esercita writer/worker di produzione su net48 con dati sintetici, API simulata bloccata, watchdog, cancellazione e ripresa. Non è un test Siemens.
5. Verificare i nuovi controlli WPF: Rapida/Completa/Mirata, campi ID/nome e checkpoint, XML validi/totale enumerato, operazione corrente, tempo totale/corrente, ultimi cinque eventi e ultimo checkpoint. Verificare DPI e tastiera.

## Prima raccolta reale di prova

1. Collegare esplicitamente il PID autorizzato. Confermare che avvio, modalità e ripresa non provochino Attach automatico.
2. Scegliere una destinazione locale nuova, con spazio disponibile, fuori dalla cartella del progetto TIA. Annotare ID export e versioni DLL/EXE.
3. Iniziare con Rapida. Controllare che non compaiano chiamate Fingerprint/CrossReference nel log e che il manifest dichiari Partial e le fasi saltate. Nessuna deduzione di completezza logica.
4. Ripetere con Completa sullo stesso progetto di prova immutato. Annotare le otto fasi da metrics.json e le operazioni lente da operations.jsonl. La durata Include il costo diagnostico: non estrapolare i tempi della demo.
5. Se una chiamata supera 30/120/300 secondi, verificare i tre avvisi e il tempo UI che continua ad avanzare. Il watchdog non deve accedere a TIA o annullare la chiamata. Non occorre provocare un blocco su un progetto aziendale per validare questa funzione: il runner fornisce un blocco simulato.
6. In caso di attesa reale conservare la cartella completa, incluse le ultime righe JSONL e i checkpoint. L'ultimo Start senza End indica la chiamata/scopo da indagare, non necessariamente il nome dell'ultimo XML completato. Correlare sequenza e OperationId, non solo il testo UI.

## Annullamento, interruzione e ripresa

1. Richiedere annullamento durante una raccolta di prova. L'app non deve interrompere forzatamente un'API in corso; deve osservare il token prima della successiva operazione. Lo stato persistito deve diventare Cancelled al ritorno.
2. Controllare che XML già completati abbiano hash e record; staging non completati e ZIP `.partial` non devono essere dichiarati validi. Dopo un arresto anomalo lo stato può restare Incomplete.
3. Indicare la cartella precedente nel campo di ripresa: la nuova esportazione deve usare una destinazione differente e riscoprire l'inventario, senza saltare semplicemente i primi 48 oggetti.
4. Finché il fingerprint reale non ha copertura attestata, attendersi riesportazione: non è un guasto della ripresa ma una scelta conservativa. Non abilitare ReliableForXml nel codice senza prove della sua semantica e dell'identità del progetto.
5. Con fixture sintetici/runner verificare riuso solo a prove corrispondenti; modificare hash/file, togliere il file, cambiare identità/API/linguaggio/protezione o eliminare le prove deve provocare riesportazione. Non alterare l'installazione Siemens o un progetto PLC per simulare questi casi.
6. Confrontare una raccolta ripresa e una nuova dello stesso progetto stabile: inventario, esiti e contenuto; ignorare soltanto metadati esplicitamente documentati, non differenze di logica.

## Mirata, ZIP e compatibilità

1. Indicare un nome esatto o ID da un inventario di prova; verificare che vengano esportati soltanto gli oggetti selezionati. Se il nome è comune a più scope, verificare tutti gli ID inclusi o usare un ID specifico.
2. Confrontare Selection, ExcludedIds ed export-inventory.json: totale = inclusi + esclusi, nessuna dipendenza implicita promessa.
3. Un selettore inesistente deve fallire senza produrre un manifest completo. Il filtro semantico V0.9 non deve essere applicato su una raccolta già Mirata/Rapida.
4. Aprire uno ZIP V0.9 già verificato, poi uno V1.0 Completa e uno Mirata/Rapida: controllo hash, navigazione e avvisi di copertura corretti.
5. Su copie sintetiche verificare modifica dell'XML dopo checkpoint/prima o durante packaging: nessun nuovo ZIP finale in caso di discordanza. Un archivio può riportare errori noti come Incomplete, ma non deve dichiarare raccolta completa.
6. Alla disconnessione/chiusura la dashboard deve rilasciare solo risorse possedute e lasciare aperti TIA e il progetto, senza salvataggi, compilazioni PLC, download o operazioni online.

Registrare risultati e log come nuova evidenza Windows; non sostituirli con i risultati cloud o le prove V0.9. Il primo obiettivo sull'incidente è conoscere la chiamata realmente in attesa, prima di attribuire una causa o promettere una correzione dei tempi.
