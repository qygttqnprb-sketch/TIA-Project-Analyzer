# Verifica Windows · V0.9

Compilazione Siemens, WPF demo e progetto PLC reale sono verifiche distinte.

## Build e demo

```powershell
dotnet build TIA-Project-Analyzer.slnx -c Debug -p:Platform=x64 -p:EnableSiemensOpenness=true
dotnet build TIA-Project-Analyzer.slnx -c Release -p:Platform=x64 -p:EnableSiemensOpenness=true
dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj
dotnet build TiaAnalyzer.WpfChecks/TiaAnalyzer.WpfChecks.csproj -c Debug -p:Platform=x64 -p:EnableSiemensOpenness=true
.\TiaAnalyzer.WpfChecks\bin\x64\Debug\net48\TiaAnalyzer.WpfChecks.exe
```

Il runner WPF verifica binding, nessun Attach automatico, selezione esplicita,
demo, ZIP, ricerca, confronto, disconnessione e chiusura; produce immagini fuori
schermo in `artifacts/`. `SmokeCheck.ps1` rimane opzionale dove gli script sono già
autorizzati. Non cambiare execution policy per eseguirlo.
Verificare manualmente DPI 125%/150%, tastiera e finestra minima: il rendering
fuori schermo non copre tutte le configurazioni desktop.

## Autorizzazione e Attach reale

1. Verificare il gruppo con `whoami /groups` e il controllo nell'app. Se manca,
   seguire [OPENNESS-AUTHORIZATION.md](OPENNESS-AUTHORIZATION.md).
2. Dopo l'intervento IT, disconnettersi da Windows e accedere nuovamente.
3. Senza TIA aperto, Rileva deve dare lista vuota; con una/più istanze, confrontare
   PID, eseguibile e progetto con Gestione attività e TIA.
4. Aprire manualmente un progetto di prova autorizzato, con PLC offline. Selezionare
   il PID e confermare; nessuna selezione/connessione automatica.
5. Provare rifiuto del dialogo, AuthorizationDenied e retry, poi consenso autorizzato.
   Confrontare inventario e TIA. Durante il dialogo la UI non deve abbandonare Attach.
6. Nessuna operazione deve concedere diritti o modificare AllowList/policy.

## Export reale

1. Progetto noto con OB/FB/FC/DB/UDT/tag, cartelle annidate e, se disponibili,
   più PLC, unità, tipi/blocchi di sistema e un blocco protetto autorizzato.
2. Esportare XML/ZIP; confrontare conteggi, nomi, numeri blocco, linguaggi, dichiarazioni,
   commenti e reti con TIA. Verificare XML originali e SHA256.
3. Controllare Failed, InterfaceOnly e problemi; nessuna protezione rimossa o compilazione PLC.
4. Cercare un simbolo noto; controllare reti, chiamate/istanze e scope. In LAD/FBD
   controllare contatti negati e fili nell'XML originale.
5. Annullare tra export: nessuno ZIP troncato. Riprovare in una cartella nuova.
6. Confrontare due snapshot invariati e uno dopo una modifica manuale al solo
   progetto di prova: verificare invariati, metadati, aggiunte/rimozioni e modifiche.
7. Nessun file nel progetto o richiesta di salvataggio causata dall'app; file solo
   nella destinazione scelta. Nessuna DLL Siemens nell'output.

## Verifiche V0.6–V0.8 sul progetto di prova

1. Confrontare crossreferences.json con i riferimenti incrociati di TIA: accesso,
   posizione, nome, indirizzo e provenienza. Verificare anche membri senza oggetto
   UnderlyingObject e servizi non disponibili/protetti: esito esplicito, nessuno sblocco.
2. Eseguire due raccolte prima senza riuso XML, poi con riuso attivo. Confrontare
   tutti gli XML e gli hash; modificare codice, interfaccia, commento, UDT dipendente
   e proprietà separatamente. Ogni modifica pertinente deve invalidare la cache.
3. Provare un blocco non consistente: nessuna compilazione automatica. Per assenza
   di impronte usare export normale, con errore per oggetto se non è esportabile.
4. Verificare le guardie IF/ELSIF/ELSE e i parametri tra chiamanti/istanze su casi noti.
   IN_OUT, output associati, cicli, salti e reti grafiche non supportate devono dichiarare copertura parziale.
   Per LAD/FBD confrontare serie/paralleli, polarità, porte A/O e bobine con TIA.
   Verificare Set/Reset senza inferire il mantenimento dello stato o le priorità.
   In writers.md controllare i siti tra blocchi e la separazione delle variabili locali.
5. Confrontare ZIP completo e mirato: inventario/esclusioni espliciti, nessuna falsa
   cancellazione nel confronto, selezione ampliata quando la copertura è incerta.
6. Cambiare un consenso SCL, un contatto LAD e l'ordine delle reti: report prima/dopo
   coerente e nessuna equivalenza funzionale presunta. Alterare un XML del solo test:
   il confronto deve rifiutarlo perché non corrisponde al manifest.
7. Confrontare tempi freddi/caldi e memoria sui progetti rappresentativi. I risultati
   della piccola demo non misurano le prestazioni di un progetto reale.

## Rilascio ed esiti

Per la consultazione offline, disconnettere TIA e aprire lo ZIP prodotto in
precedenza. Verificare provenienza, data, avvisi, simboli, tracce, scritture e
anteprima della rete. Una copia alterata deve dare errore mantenendo il contesto
precedente. Il sorgente non deve essere modificato o estratto dall'app.
Vedere [OFFLINE-SNAPSHOTS.md](OFFLINE-SNAPSHOTS.md).

Provare disconnessione, cambio modalità e chiusura separatamente: TIA e progetti
restano aperti. Un'istanza di prova terminata tra rilevamento/Attach deve dare
errore gestito e nuovo rilevamento; un errore di rilascio deve consentire retry.

Registrare Windows, SDK/Visual Studio, TIA/DLL, token/gruppo, build, test, PID/progetto,
conteggi/problemi, reti, simboli, confronto e rilascio. Fino alla prova reale riuscita
indicare **adapter compilato, demo verificata, Attach/export reale ancora da validare**.
