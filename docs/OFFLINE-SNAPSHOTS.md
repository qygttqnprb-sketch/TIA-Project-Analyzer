# Consultazione offline · V0.9

Nella sezione di ricerca scegliere uno ZIP oppure un manifest JSON e premere
**Apri snapshot offline**. Non servono rilevamento, consenso di Attach o una
sessione TIA. La connessione eventualmente aperta resta separata dal materiale
consultato. La provenienza DEMO/reale è letta dal manifest e mostrata accanto al
percorso e alla data di raccolta, indipendentemente dalla modalità di connessione.

L'app ricalcola dai file originali ricerca, condizioni, chiamate e scritture.
Non usa analysis.json o writers.json come risultati autorevoli e non estrae
l'archivio. Uno ZIP non viene modificato durante l'apertura o l'anteprima.

## Integrità e copertura

Ogni XML dichiarato Exported/InterfaceOnly deve essere presente una sola volta e
corrispondere al SHA256 del manifest. Si controllano anche le dimensioni quando
sono dichiarate. Un file alterato o mancante interrompe l'apertura e conserva il
contesto verificato precedente. Hash coerenti indicano corrispondenza con il manifest,
non una firma digitale o una prova dell'origine del pacchetto.

Gli oggetti dichiarati Failed rimangono nell'inventario con copertura mancante.
I pacchetti mirati conservano conteggi ed esclusioni: gli oggetti esclusi non
vengono cercati tra file aggiuntivi dell'archivio. Avvisi e limitazioni sono
visibili nella dashboard. I dati rimangono offline; nessuno stato corrente del
PLC viene dedotto dalla data del pacchetto.

Sono rifiutati percorsi non validi, alias di file dovuti alle maiuscole Windows,
manifest duplicati, selezioni incoerenti e riferimenti malformati. Per la lettura
da cartella, file o sottocartelle che siano link/junction vengono rifiutati.
Il parser XML disabilita DTD ed entità esterne.

Limiti: 100000 voci ZIP, manifest da 64 MiB, 128 MiB per XML prima del parsing,
64 MiB di caratteri XML e 2 GiB di lettura XML totale per apertura.
Il lettore mantiene un indice dell'archivio e carica un XML alla volta.
Non scrive una cache o report nella cartella di origine.

## Ricerca e sorgenti

La ricerca dei simboli, le tracce SCL/LAD/FBD e l'indice delle scritture funzionano
anche dopo la disconnessione da TIA. Aprire un archivio non collega né modifica
un progetto. Una nuova esportazione aggiorna il contesto alla raccolta locale.

Il selettore elenca blocco, PLC, unità, rete e linguaggio delle prime 5000 reti.
**Leggi sorgente XML della rete** mostra il NetworkSource originale, previa
verifica del file rispetto al contesto caricato. Se lo snapshot cambia dopo
l'apertura, l'anteprima con hash diverso viene rifiutata. La visualizzazione è
limitata a 16000 caratteri, con rinvio all'XML completo.

L'apertura e la lettura sono annullabili con **Annulla operazione**.
Le azioni esplicite Segui condizioni e Mostra scritture salvano i relativi
report nella destinazione di lavoro scelta, separata dalla lettura dell'archivio.

## Verifica

I controlli sintetici coprono ZIP e cartella, report serializzati non affidabili,
integrità, file mancanti/duplicati, percorsi non sicuri, manifest meno recenti,
DTD, annullamento, selezioni mirate, sorgenti cambiate e contesto UI precedente.
Sono eseguibili su .NET 10 e sul runtime net48 dell'app:

```powershell
.\TiaAnalyzer.WpfChecks\bin\x64\Debug\net48\TiaAnalyzer.WpfChecks.exe --offline-only
```

La verifica con diagnostica Siemens indisponibile usa una sonda simulata.
L'export e il contenuto di progetti TIA V21 reali restano da collaudare.
