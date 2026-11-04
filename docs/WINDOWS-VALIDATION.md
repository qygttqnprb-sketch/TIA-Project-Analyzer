# Verifica Windows · V0.5

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

## Rilascio ed esiti

Provare disconnessione, cambio modalità e chiusura separatamente: TIA e progetti
restano aperti. Un'istanza di prova terminata tra rilevamento/Attach deve dare
errore gestito e nuovo rilevamento; un errore di rilascio deve consentire retry.

Registrare Windows, SDK/Visual Studio, TIA/DLL, token/gruppo, build, test, PID/progetto,
conteggi/problemi, reti, simboli, confronto e rilascio. Fino alla prova reale riuscita
indicare **adapter compilato, demo verificata, Attach/export reale ancora da validare**.
