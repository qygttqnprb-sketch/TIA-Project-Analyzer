# Esportazione PLC · V0.3

Dopo il collegamento esplicito, **Esporta XML** crea una nuova sottocartella nella
destinazione scelta. Le chiamate Siemens avvengono sullo stesso worker STA di Attach
e Dispose. La UI blocca cambio modalità e disconnessione durante l'esportazione;
Annulla viene osservato tra le operazioni Siemens, senza abbandonare chiamate in corso.

L'adapter attraversa dispositivi, gruppi hardware, sottocartelle di programma,
blocchi di sistema, UDT, tabelle variabili, Software Units e Safety Units esposte
dall'API. Usa `Export(file, ExportOptions.WithDefaults)` su blocchi, tipi e tabelle.
Nessuna compilazione, apertura, importazione, modifica, sblocco o operazione online.
Tenere il PLC offline e non modificare il progetto durante la raccolta: la lettura
sequenziale non costituisce uno snapshot atomico.

Ogni XML ha un'identità basata su progetto, PLC, gruppo, tipo e nome. I percorsi
mantengono cartelle separate e nomi sanificati con hash per evitare collisioni
su Windows. I nomi originali restano nel manifest. Cartelle non vuote non vengono
riutilizzate. Un blocco non esportabile produce un esito Failed e una voce Issues;
i file parziali non vengono dichiarati utilizzabili. I blocchi protetti sono
indicati come InterfaceOnly. Non viene mai rimossa una protezione.

`manifest.json` contiene schema, provenienza DEMO/reale, data UTC, file, SHA256,
dimensioni, esiti, problemi, inventario hardware e limitazioni. `components.json`
contiene lo stesso inventario in un file dedicato. L'assenza del manifest dopo
un errore/annullamento indica una raccolta incompleta. Tutti i dati restano locali.

La demo produce sei XML sintetici comprendenti un motore con consensi SafetyOK,
DriveReady e JamDetected, senza leggere alcun progetto Siemens.

Verificato il 4 novembre 2026: compilazione dell'adapter contro Base/Step7 V21
installate e test automatici demo. **Esportazione da un progetto TIA reale ancora
da verificare dopo l'autorizzazione IT.** HMI, allarmi e parametri degli azionamenti
sono estensioni successive; l'inventario hardware non ne sostituisce l'export.

Fonti V21: [blocchi](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/exporting-blocks),
[UDT](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/exporting-user-data-type),
[tabelle variabili](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/tag-tables/exporting-plc-tag-tables).
