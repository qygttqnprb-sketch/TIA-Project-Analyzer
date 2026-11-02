# Verifica manuale Windows · V0.1

Queste verifiche richiedono un PC Windows e non sono state eseguite nel cloud. L'integrazione Siemens resta non verificata; V0.1 non implementa il collegamento reale.

## Compilazione e avvio

1. Installare il workload desktop .NET di Visual Studio, il targeting pack net48 e un SDK compatibile con `.slnx` (SDK 10 usato nel cloud).
2. Aprire la soluzione, selezionare Debug e x64, compilare. Ripetere con Release x64.
3. Avviare `TiaAnalyzer.App` con F5. Controllare tema scuro, testi leggibili, scorrimento, ridimensionamento e navigazione tramite Tab/Spazio. Provare il ridimensionamento a 125% e 150% DPI.
4. Nella finestra Output di Visual Studio verificare l'assenza di errori di binding WPF. La diagnostica deve partire senza clic iniziale e senza bloccare l'interfaccia.

## Diagnostica reale (soltanto metadati)

1. Senza Siemens, attendersi errori per directory e DLL mancanti, con connessione reale disabilitata e nessuna eccezione non gestita.
2. Sul PC con V21, verificare percorso documentato, processo x64, identità `Siemens.Engineering.Base` e `Siemens.Engineering.Step7`, versione principale 21. Non rinominare o sostituire DLL dell'installazione per provare errori.
3. Anche con tutti i metadati corretti, la UI deve indicare **integrazione Siemens non verificata** e mantenere disabilitata la connessione reale.
4. Ripetere il controllo; durante l'attesa non devono essere disponibili un secondo controllo o il cambio modalità.

## Demo e reset

1. Attivare Modalità dimostrativa: tre controlli MOCK, etichetta DEMO, nessun accesso Siemens.
2. Connettere l'istanza dimostrativa: stato MOCK connesso, sei componenti marcati DEMO, inclusi controllore, OB, FB, DB e HMI sintetici.
3. Disconnettere: stato Disconnesso, nessun componente residuo; riconnettere e verificare che non si creino duplicati.
4. Tornare alla modalità reale mentre la demo è connessa: sessione e dati sintetici devono sparire, connessione reale disabilitata.
5. Verificare XML e ZIP disabilitati in entrambe le modalità e assenza di file esportati.

## Smoke test ripetibile

Dopo una build Debug x64, da PowerShell STA eseguire `powershell.exe -NoProfile -STA -File .\SmokeCheck.ps1`. L'opzione `-RequireSiemens` aggiunge il requisito dei metadati V21; non effettua connessioni o chiamate API Siemens. Se la execution policy blocca lo script, seguire la checklist manuale senza aggirare la policy aziendale.

Registrare versione Windows/Visual Studio/SDK, esito Debug e Release, errori di binding eventuali, esito demo e metadati rilevati. L'accesso Openness, i permessi e il collegamento a un'istanza già aperta saranno verifiche separate quando esisterà un adattatore reale. Non aprire, compilare, scaricare o salvare progetti PLC per validare questa versione.
