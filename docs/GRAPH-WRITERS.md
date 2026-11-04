# Consensi LAD/FBD e siti di scrittura · V0.8

La raccolta genera condizioni booleane anche per le reti grafiche del sottoinsieme
descritto qui. Gli XML originali restano l'evidenza da consultare: il report è
pseudocodice e non viene importato o eseguito. Sono disponibili soltanto dati offline.

## Reti interpretate

Il parser accetta FlgNet v4 con linguaggio LAD o FBD esplicito:

- LAD: contatti in serie, contatti negati, rami paralleli con uscite Contact
  sullo stesso filo, collegamenti tramite porta O e bobine di assegnazione.
- FBD: porte A/O con cardinalità da 2 a 64, negazione dei pin di ingresso/uscita,
  assegnazioni tramite Coil oppure collegamento dell'uscita a una variabile.
- Bobine SCoil/RCoil: evidenze Set/Reset condizionate dal segnale di ingresso.
  Quando l'ingresso è falso il report non introduce una scrittura: il valore
  precedente, la priorità e la scansione non vengono simulati.
- Operandi simbolici semplici locali/globali e costanti booleane TRUE/FALSE.
  I confini dei componenti quotati sono conservati.

Per esempio, un ramo Auto in parallelo a Manual seguito dai contatti SafetyOK
e JamDetected negato produce l'evidenza:

```text
MotorRun := (Auto OR Manual) AND SafetyOK AND NOT JamDetected
```

Il risultato indica una dipendenza nel programma, non lo stato corrente del motore.
La traccia conserva linguaggio, ID rete e UId della parte; non inventa una riga SCL.
Il testo SCL già supportato dalla V0.7 continua a usare le righe ricostruite dai token.

## Copertura e limiti

Timer, fronti, confronti grafici, SR/RS come istruzioni composite, MOVE, chiamate
grafiche, GRAPH e istruzioni non riconosciute restano fuori dal modello booleano.
Si conservano, ove collegabili senza ambiguità, le destinazioni delle uscite di
chiamata dichiarate Output/InOut, con valore non modellato e copertura parziale.
Questi siti non implicano un'associazione risolta tra parametri e istanze.

Scope assoluti, indirizzamento indiretto/indicizzato, schema diverso da v4, UId
duplicati, pin ambigui, fili mancanti, cicli e attributi non riconosciuti producono
copertura parziale. Anche le scritture note in una rete parziale vengono marcate
PARZIALE. Non si assume TRUE per un ingresso mancante o sconosciuto.

Limiti per rete: 2048 oggetti, 8192 fili, 64 livelli, 20000 visite e circa 8 KiB
per l'espressione ricostruita. Le operazioni locali controllano la cancellazione.
Con Report compatti l'analisi legge il NetworkSource originale senza conservarne
una seconda copia. La cache analisi cambia versione e ricalcola gli indici globali.

## Scritture multiple

`writers.json` e `writers.md` elencano destinazione, linguaggio, operazione,
valore, condizione, blocco, rete e posizione di ogni sito estratto. Sono inclusi
negli ZIP completi e mirati. Nella dashboard usare **Mostra scritture** dopo una
ricerca; lasciando il campo vuoto si consultano tutti i simboli.

- SingleSite: un sito nel materiale disponibile.
- MultipleSites: più siti nello stesso blocco, anche in rami IF/ELSE esclusivi.
- MultipleBlocks: siti in più blocchi del medesimo ambito.

Queste classificazioni non sono diagnosi di conflitto e non stabiliscono quale
scrittura prevalga. Una bobina normale scrive il risultato booleano anche quando
falso; Set/Reset scrivono soltanto quando abilitati.

Le variabili locali sono separate per blocco: lo stesso nome in due FB non è la
stessa memoria. Per gli FB non si fondono automaticamente le diverse istanze.
I globali sono separati per progetto, PLC e Software Unit; alias e sovrapposizioni
di indirizzo non vengono risolti. Oggetti protetti, reti mancanti e sintassi non
modellata possono nascondere altri siti.

I cross-reference Siemens di accesso Write/ReadWrite sono riportati separatamente,
con la provenienza originale. Duplicati identici raccolti interrogando oggetti
diversi non aumentano i conteggi. Non vengono sommati ai siti XML o associati a
reti/istanze sulla sola base del nome.

I pacchetti mirati contano solo i blocchi inclusi; inventario e manifest dichiarano
le esclusioni. Se la copertura non basta, la selezione resta conservativa.

## Verifica

La suite usa reti sintetiche: tabelle di verità, negazioni, rami paralleli,
Set/Reset, scritture tra blocchi, separazione degli ambiti, dati malformati,
cache, report compatti e cancellazione. Gli stessi controlli di rete/scrittura
sono eseguibili su net48:

```powershell
dotnet build TiaAnalyzer.WpfChecks/TiaAnalyzer.WpfChecks.csproj -c Debug -p:Platform=x64 -p:EnableSiemensOpenness=true
.\TiaAnalyzer.WpfChecks\bin\x64\Debug\net48\TiaAnalyzer.WpfChecks.exe --graph-only
```

Resta necessario confrontare le evidenze con reti esportate da un progetto di
prova V21 autorizzato prima dell'uso operativo. Nessun test collega un PLC reale.

Riferimenti di formato: [esempi Siemens MAC dei pin booleani e collegamenti FlgNet](https://siemens.github.io/modular-application-creator-use-cases/html/class_m_a_c__use__cases_1_1_model_1_1_use_cases_1_1_bit_logic_networks.html),
[export XML dei blocchi V21](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/exporting-blocks).
