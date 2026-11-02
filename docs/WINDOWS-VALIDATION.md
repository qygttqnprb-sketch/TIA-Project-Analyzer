# Verifica Windows · V0.2

La V0.1 è stata avviata su Windows x64 dall'utente, con net48 e DLL Base/Step7 21.0.0.0 rilevate. **Questo non verifica Attach o lettura Siemens della V0.2.** I controlli seguenti sono da eseguire sul PC Windows con TIA Portal V21.

## Build e binding

1. Usare Visual Studio, workload desktop .NET, targeting pack net48 e SDK compatibile con slnx.
2. Seguire [OPENNESS-V21.md](OPENNESS-V21.md): abilitare `EnableSiemensOpenness=true`, reference locali Base e Step7 con `Private=false`, ricompilare Debug e Release x64. Per F5 usare l'opt-in locale `Directory.Build.props` ignorato da Git.
3. Confrontare i membri elencati nel documento con l'API reference ufficiale V21 installata. Il file `SiemensBackend.V21.cs` non è stato compilato nel cloud. Registrare qualsiasi errore di firma/API e risolverlo prima della prova di connessione.
4. Avviare F5. Verificare risoluzione delle dipendenze dall'installazione, stato «Adapter V21 incluso», nessun errore di binding nella finestra Output, tema scuro, navigazione Tab/Spazio, scorrimento e DPI 125%/150%.
5. Verificare la diagnostica automatica: x64, directory, identità e versione principale 21. **Non deve apparire alcuna autorizzazione Openness o collegamento automatico all'avvio.**

## Demo e smoke test

1. Attivare la demo, premere Rileva, verificare un'istanza sintetica e nessuna selezione automatica.
2. Selezionare l'istanza, collegarsi: sei componenti DEMO. Disconnettere: sessione e componenti svuotati. Riconnettere e tornare al reale: nessun dato sintetico residuo né Attach reale automatico.
3. XML e ZIP disabilitati in entrambe le modalità.
4. Dopo build Debug, eseguire `powershell.exe -NoProfile -STA -File .\SmokeCheck.ps1`. Il flag `-RequireSiemens` aggiunge solo il requisito dei metadati V21. Se la execution policy blocca lo script, usare la checklist senza aggirare la policy.

## Rilevamento reale

1. Senza TIA Portal aperto: premere Rileva, attendersi «Nessuna istanza TIA disponibile», lista vuota e collegamento disabilitato.
2. Aprire manualmente TIA Portal V21 con un **progetto di prova**, senza connessioni online ai PLC. Non utilizzare progetti aziendali nella prima prova.
3. Rilevare di nuovo: confrontare PID con Gestione attività; controllare versione dell'eseguibile, percorso e progetto associato dove disponibile.
4. Aprire una seconda istanza di prova e verificare due righe distinte. Nessun PID deve essere scelto automaticamente.
5. Selezionare un PID: il pulsante di collegamento reale resta disabilitato fino alla conferma esplicita del progetto di prova. Cambiando selezione, la conferma deve azzerarsi.
6. Chiudere manualmente un'istanza dopo il rilevamento e prima di Attach: verificare errore gestito, UI responsiva e possibilità di ripetere il rilevamento.

## Attach e autorizzazioni

1. Verificare con l'amministratore i prerequisiti Openness V21 e il gruppo locale Siemens TIA Openness; non modificare gruppi o policy tramite l'app.
2. Selezionare il PID di prova, confermare e collegarsi. Rispondere manualmente al dialogo Siemens. Durante l'attesa UI responsiva e cambio modalità/selezione bloccati. Un tentativo di chiudere la dashboard deve richiedere il completamento dell'operazione in corso, senza abbandonare un Attach.
3. **Negare** prima l'autorizzazione: stato di errore/rifiuto, log diagnostico, nessun dato parziale. Ripetere il comando e **consentire** solo per il progetto di prova. Se Siemens ricorda una decisione, seguire le sue istruzioni aziendali per una prova separata; non aggirare la policy.
4. Verificare elenco dei soli progetti già aperti, nomi/percorsi, dispositivi anche nei gruppi e software STEP 7 dei PLC dove presente. Nessun progetto deve essere aperto dall'app.
5. Se l'istanza non ha progetti aperti, Attach può riuscire con zero componenti; non deve creare o aprire progetti.
6. Verificare che nessuna operazione online, compilazione PLC, download, scrittura DB, modifica hardware o salvataggio sia proposta o invocata.

## Rilascio ed errori

1. Disconnettere: dashboard svuotata; TIA Portal e il progetto di prova devono restare aperti, senza richiesta di salvataggio provocata dall'app.
2. Riconnettere, quindi cambiare modalità: stesso rilascio, nessun processo TIA terminato.
3. Riconnettere, poi chiudere la dashboard: TIA e i progetti restano aperti. Ripetere più cicli per osservare eventuali handle residui o autorizzazioni inattese.
4. Terminare manualmente solo un'istanza **di prova** mentre la dashboard è collegata; tentare Disconnetti. Verificare errore gestito e retry senza arresto dell'app. Registrare il comportamento V21 di Dispose sull'istanza terminata.
5. Se il rilascio fallisce, l'app deve mantenere il comando Disconnetti disponibile per un nuovo tentativo. Non forzare la chiusura di TIA per correggere un problema dell'analyzer.
6. Verificare log in memoria (massimo 100 eventi), nessun nuovo file nei progetti e nessuna DLL Siemens nel repository/output applicativo.

## Registrazione esiti

Registrare versione Windows/Visual Studio/SDK/TIA, versione completa DLL, compilazione abilitata Debug/Release, esito resolver, rilevamento zero/più istanze, rifiuto/consenso, confronto dispositivi/PLC, disconnessione e chiusura dashboard. Finché questi passaggi non sono stati provati, descrivere la connessione Siemens come **implementata ma non verificata su Windows V21**.
