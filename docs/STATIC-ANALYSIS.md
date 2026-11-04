# Analisi statica e confronto · V0.5

Ogni export genera `analysis.json`, `symbols.json`, `dependencies.dot`, `logic.md`
e `blocks/<id>.md`. I documenti includono file originali, ID reti, testo SCL ricostruito
e, nei report dettagliati, NetworkSource integrale. Con Report compatti il contenuto
non viene duplicato negli indici: rimane negli XML originali collegati. Gli XML non vengono riscritti: contatti, polarità, fili
e dati GRAPH restano disponibili per ChatGPT.

## Chiamate e simboli

L'analizzatore legge CallInfo, istanze DB, dichiarazioni dei membri/tag, indirizzi
e Access delle reti. Integra candidati testuali SCL distinguendoli dall'evidenza XML.
Esclude commenti, commenti annidati e stringhe. Ricostruisce token, spazi, righe,
costanti e forme comuni di chiamate/parametri SCL.

Le dipendenze si risolvono entro lo stesso progetto, PLC e Software Unit.
Nomi ambigui/esterni restano irrisolti. Le multi-istanze locali sono collegate al
tipo dichiarato quando disponibile, le istanze DB al proprio FB. L'indice indica
origine/rete/evidenza, senza attribuire significato read/write non dimostrato.
Quando disponibili, i cross-reference ufficiali Siemens aggiungono accesso e
posizione con provenienza esplicita; vedere [cache e prestazioni](PERFORMANCE-CACHE.md).
La UI mostra al massimo 200 risultati con conteggio completo e rimando al JSON.

La V0.7 aggiunge [traccia SCL, selezione mirata e confronto reti](CONDITIONS-FOCUS-DIFF.md).
È estrazione di evidenze, non un compilatore Siemens: accessi complessi, template,
riferimenti tra unità e XML non coperti richiedono l'originale. Non vengono valutati
stati runtime, timer o una macchina a stati completa. ChatGPT deve seguire reti e
chiamate, citare l'evidenza e indicare i segnali necessari a confermare le ipotesi.
Il parser disabilita DTD/entità esterne, limita ogni XML a 64 MiB di caratteri e
segnala file non analizzabili senza cancellarli dal pacchetto.

## Confronto

Selezionare due manifest o ZIP con Sfoglia, poi Confronta. Una cartella nuova
contiene `comparison.json` e `comparison.md`.

- Added/Removed: identità aggiunta o assente.
- Changed: contenuto diverso.
- ExportMetadataOnly: hash raw diverso ma XML normalizzato identico, rimuovendo
  solo DocumentInfo e indentazione XML. Non è equivalenza semantica PLC.
- AvailabilityChanged/Unavailable: copertura cambiata o oggetti non esportati.

Il confronto ricontrolla hash/dimensioni di ogni XML dichiarato utilizzabile senza
estrarre file dallo ZIP. Valida percorsi/identità e limita il manifest decompresso a 64 MiB.
Rifiuta confronti DEMO/reale. Scope differenti e snapshot parziali producono avvisi.
Rinomine/spostamenti possono risultare come rimozione più aggiunta.

Documentazione ufficiale V21: [SCL](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/export/import-of-scl-blocks),
[chiamate SCL](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/export/import-of-scl-call-blocks).
