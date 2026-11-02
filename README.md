# TIA Project Analyzer · V0.2

Applicazione Windows WPF, C# 7.3, .NET Framework 4.8, x64. Dashboard scura MVVM per diagnostica e analisi in sola lettura delle istanze Siemens TIA Portal V21 già aperte, con adapter demo indipendente.

La V0.1 è stata compilata e avviata correttamente sul PC Windows dall'utente, con DLL V21 rilevate. **La connessione Siemens V0.2 non è stata compilata né verificata nel cloud: richiede compilazione e prova su Windows con TIA Portal V21.**

## Avvio su Windows

Prerequisiti: Visual Studio con workload desktop .NET, targeting pack .NET Framework 4.8, SDK compatibile con `.slnx` (SDK 10 usato nel cloud). Aprire `TIA-Project-Analyzer.slnx`, selezionare `TiaAnalyzer.App`, Debug x64.

Build demo/diagnostica, adapter Siemens escluso per default:

```powershell
MSBuild TIA-Project-Analyzer.slnx /restore /t:Rebuild /p:Configuration=Debug /p:Platform=x64
```

Build con adapter reale e DLL installate localmente:

```powershell
MSBuild TIA-Project-Analyzer.slnx /restore /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /p:EnableSiemensOpenness=true
```

Per usare F5 con adapter reale abilitato, vedere il file locale `Directory.Build.props` documentato in [docs/OPENNESS-V21.md](docs/OPENNESS-V21.md), insieme a riferimenti, resolver e superficie API da verificare.

Le DLL Siemens non vengono incluse nel repository né copiate nell'output. Reference locali con `Private=false`:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
  Siemens.Engineering.Base.dll
  Siemens.Engineering.Step7.dll
```

## Flusso esplicito di collegamento

1. All'avvio viene eseguita soltanto la diagnostica dei metadati. Nessun Attach automatico.
2. Premere **Rileva istanze TIA / demo** per visualizzare PID, versione eseguibile, percorso e progetto associato se disponibile. Nessuna selezione automatica.
3. Selezionare manualmente l'istanza. Per il reale, confermare l'uso di un progetto di prova e premere **Collega istanza selezionata**.
4. Rispondere all'eventuale richiesta Openness nel dialogo Siemens. L'app non concede autorizzazioni automaticamente.
5. Dopo Attach vengono letti esclusivamente i progetti già aperti, i dispositivi, gli elementi hardware e il software STEP 7 dove disponibile. La dashboard mostra progetto, percorso gerarchico, tipo e stato.
6. **Disconnetti** rilascia il solo handle Attach. TIA Portal e i progetti già aperti devono restare attivi. Anche cambio modalità e chiusura della dashboard rilasciano le risorse possedute.

La modalità dimostrativa segue lo stesso flusso di rilevamento/selezione, con sei componenti sintetici marcati DEMO e nessun accesso Siemens. Errori e rifiuti compaiono nello stato e nel log in memoria. Durante un'operazione il cambio modalità, la selezione e le altre operazioni sono bloccati. Prima di chiudere la dashboard completare l'eventuale autorizzazione Siemens in attesa.

Non sono implementati apertura automatica di progetti, modifica hardware, scrittura DB, compilazione PLC, download, accesso online, salvataggio o esportazione. XML/ZIP rimangono contratti futuri e pulsanti disabilitati.

## Architettura

- `TiaAnalyzer.App`: WPF/MVVM, comandi asincroni, selezione e consenso; nessun tipo Siemens nel ViewModel.
- `TiaAnalyzer.Core`: DTO, contratti in sola lettura, categorie di errore e diagnostica in memoria.
- `TiaAnalyzer.Openness`: coordinatore sessione e worker STA; `SiemensBackend.V21.cs` tipizzato condizionale, fallback esplicito senza DLL e `DemoBackend` separato.
- `TiaAnalyzer.Export`: soli piani XML/ZIP, nessuna scrittura.
- `TiaAnalyzer.Checks`: test portabili con backend simulati; non certificano runtime Windows net48, binding WPF o API Siemens.

## Verifiche

```bash
dotnet build TIA-Project-Analyzer.slnx -c Debug -p:Platform=x64
dotnet build TIA-Project-Analyzer.slnx -c Release -p:Platform=x64
dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj
```

Le build cloud compilano **solo il fallback** dell'adapter Siemens. I test collegano i sorgenti su runtime .NET 10 e coprono zero/più istanze, selezione, Attach simulato, rifiuto, errori, rilascio, retry, proprietà dell'handle e serializzazione.

Su Windows: `powershell.exe -NoProfile -STA -File .\SmokeCheck.ps1` verifica i binding e il flusso demo; `-RequireSiemens` richiede anche metadati V21 validi ma non esegue Attach reale. Non modificare execution policy aziendali per eseguirlo. Seguire [docs/WINDOWS-VALIDATION.md](docs/WINDOWS-VALIDATION.md) per le prove reali iniziali su un progetto di test e per registrare gli esiti.
