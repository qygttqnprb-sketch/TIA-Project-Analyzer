# TIA Project Analyzer

Soluzione WPF esistente, .NET Framework 4.8. Avvio da Visual Studio: aprire `TIA-Project-Analyzer.slnx`, selezionare `TiaAnalyzer.App` come progetto di avvio, scegliere `Debug | x64` e premere F5.

DLL Siemens installate in `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`. Non includere le DLL nel repository.

## Struttura

- `TiaAnalyzer.App`: interfaccia WPF scura, controlli asincroni e selezione Mock.
- `TiaAnalyzer.Core`: risultati diagnostici e contratti per servizi e sessioni in sola lettura.
- `TiaAnalyzer.Openness`: rilevamento locale delle DLL V21 tramite metadati; ambiente e sessione Mock.
- `TiaAnalyzer.Export`: predisposizione per la futura esportazione XML, attualmente disabilitata.

Il controllo parte automaticamente all'avvio ed è ripetibile con **Verifica disponibilità Openness**. Verifica processo x64, cartella e identità/versione delle DLL Base e Step7. Non carica le DLL Siemens e non invoca API Openness. La presenza delle librerie non certifica autorizzazioni o possibilità di collegarsi a TIA.

Selezionare **Modalità Mock**, quindi **Connetti istanza Mock**, per verificare l'interfaccia senza Siemens. Tornando alla modalità reale la sessione simulata viene azzerata. La connessione reale è intenzionalmente disabilitata in questa fase; non vengono aperti o modificati progetti TIA e non viene scritto alcun XML.

Compilazione: `MSBuild TIA-Project-Analyzer.slnx /restore /p:Configuration=Debug /p:Platform=x64` da Developer PowerShell di Visual Studio. I nuovi progetti sono librerie SDK-style net48; serve un .NET SDK installato, oltre al targeting pack .NET Framework 4.8.

Verifica funzionale locale dopo la compilazione: `powershell.exe -NoProfile -STA -File .\SmokeCheck.ps1`. Richiede l'installazione V21 nel percorso indicato; verifica metadati reali, avvio WPF, modalità Mock, connessione simulata e azzeramento della sessione.

Validazione effettuata: compilazioni Debug e Release x64 riuscite; rilevamento reale di entrambe le DLL versione 21.0.0.0; servizi Mock verificati; finestra WPF avviata e responsiva. Lo script completo `SmokeCheck.ps1` non è stato eseguito perché l'execution policy locale blocca gli script PowerShell. La policy non è stata modificata; il passaggio Mock tramite controlli UI resta da verificare manualmente con F5.
