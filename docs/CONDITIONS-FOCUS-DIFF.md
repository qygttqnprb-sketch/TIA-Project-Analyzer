# Condizioni, pacchetto mirato e confronto · V0.7

## Traccia dei consensi

Dopo la raccolta indicare un simbolo, per esempio `MotorRun`, e premere Segui
condizioni. La traccia locale produce JSON e Markdown e appare nella dashboard.
I report `conditions.json` e `conditions.md` sono inclusi in ogni pacchetto.

Il parser supporta assegnazioni con destinazione semplice, espressioni booleane
e confronti semplici, rami IF/ELSIF/ELSE annidati e associazioni nominate di
chiamata. Le guardie dei rami sono conservate: ELSE include la negazione delle
condizioni precedenti. Le chiamate attraverso DB di istanza e multi-istanze
con tipo noto collegano i parametri al chiamante. Le tracce riportano file,
blocco, rete e riga del testo SCL ricostruito, senza confonderla con la riga XML.

Esempio demo: MotorRun dipende da Auto e Permissive; Permissive legge SafetyOK,
DriveReady e JamDetected; Auto viene associato ad AutoMode da Main.
Sono evidenze sintetiche, non una diagnosi di un motore reale.

FOR/WHILE/REPEAT/CASE, salti/uscite anticipate, espressioni non interpretate,
reti grafiche fuori dal sottoinsieme V0.8, sovrapposizioni di indirizzi e alias IN_OUT/uscita non vengono
simulati. La copertura è dichiarata parziale. Cicli nelle dipendenze si fermano
con avviso; limite 16 livelli/1000 evidenze. Non vengono valutati valori runtime,
ordine globale degli OB/chiamate, timer o prevalenza dell'ultima scrittura.
Più chiamanti e istanze restano possibilità distinte da verificare.
La V0.8 aggiunge [consensi LAD/FBD e siti di scrittura](GRAPH-WRITERS.md):
le reti grafiche supportate entrano nella stessa traccia con ID rete/UId.

## ZIP mirato

Attivare Crea ZIP mirato e indicare un simbolo/blocco. La raccolta locale rimane
completa: questa funzione riduce il materiale allegato a ChatGPT, non promette
un minor tempo di accesso a TIA. Include riferimenti, chiamanti, dipendenze, DB,
tipi e tabelle tag pertinenti. Se la copertura è incerta include conservativamente
gli oggetti degli scope PLC interessati, anche quando lo ZIP risulta completo.

Il manifest dichiara Selection con query, totale, inclusi ed esclusi.
`inventory.json` conserva l'inventario originale; `focus.json` spiega la selezione;
`trace.json/md` contengono la traccia. Gli XML esclusi non entrano nell'archivio.
Le reti dei chiamanti possono menzionare altri blocchi esclusi: la selezione non
dimostra che non esistano altre cause. Query inesistenti danno errore, non uno ZIP vuoto.

## Confronto per rete

Prima del confronto vengono verificati hash e dimensioni degli XML dichiarati
utilizzabili, senza estrarli dallo ZIP. L'indice dello ZIP viene aperto una volta
per snapshot. XML mancanti, duplicati o alterati fanno fallire il confronto.
Il parser rifiuta DTD/entità esterne; letture limitate e annullabili.

Il report include prima/dopo per posizione della rete, identificatori e
fingerprint nativi cambiati quando disponibili. Per SCL vengono confrontati
token preservando stringhe; commenti/spazi sono distinti dal contenuto dei token.
Per LAD/FBD gli UId vengono rinumerati mantenendo riferimenti e collegamenti:
una polarità/connessione cambiata resta una modifica. Non viene dichiarata
equivalenza funzionale PLC. Inserimenti/spostamenti possono cambiare più posizioni.
Anteprime limitate a 8000 caratteri per lato, con rinvio agli XML completi.

Un oggetto escluso da un pacchetto mirato viene indicato NotIncludedBefore/After,
senza dichiarare una cancellazione dal PLC. L'hash XML di analisi usa ora il formato
xml-tree-v2, scritto direttamente in uno stream hash senza clonare o serializzare
l'intero albero. I confronti con hash di versioni diverse restano conservativi.
