# TIA Project Analyzer · V0.1

Applicazione Windows WPF, C# 7.3, .NET Framework 4.8, x64, per la futura analisi in sola lettura di progetti Siemens TIA Portal V21. Dashboard scura, diagnostica automatica, architettura MVVM e modalità dimostrativa con dati sintetici.

**Integrazione Siemens non verificata.** V0.1 legge esclusivamente i metadati delle DLL installate. La connessione reale resta disabilitata anche quando le librerie sono presenti. Nessuna API per aprire, modificare, compilare, scaricare o salvare progetti PLC viene invocata.

## Avvio su Windows

Prerequisiti: Visual Studio con workload **Sviluppo desktop .NET**, targeting pack **.NET Framework 4.8** e SDK .NET compatibile con `.slnx` (SDK 10 usato per la compilazione cloud). Aprire `TIA-Project-Analyzer.slnx`, impostare `TiaAnalyzer.App` come progetto di avvio, scegliere `Debug | x64`, premere F5.

Da Developer PowerShell:

```powershell
MSBuild TIA-Project-Analyzer.slnx /restore /p:Configuration=Debug /p:Platform=x64
powershell.exe -NoProfile -STA -File .\SmokeCheck.ps1
# Facoltativo: richiede anche i metadati Siemens V21 corretti.
powershell.exe -NoProfile -STA -File .\SmokeCheck.ps1 -RequireSiemens
```

Lo smoke test base funziona anche senza Siemens; con `-RequireSiemens` richiede librerie V21 rilevabili. Nessuna modifica alla execution policy viene effettuata. Se gli script sono bloccati, eseguire i controlli manuali in [docs/WINDOWS-VALIDATION.md](docs/WINDOWS-VALIDATION.md).

Percorso delle librerie sul PC Windows:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
  Siemens.Engineering.Base.dll
  Siemens.Engineering.Step7.dll
```

Non copiare le DLL nel repository. Il probe verifica processo x64, directory, leggibilità dei metadati, nome assembly atteso e versione principale 21. La presenza delle DLL non dimostra autorizzazioni, compatibilità completa o accesso a un'istanza TIA.

## Flusso V0.1

All'avvio la diagnostica parte automaticamente. Ogni componente mostra stato e dettaglio. **Modalità dimostrativa** usa un probe indipendente e abilita **Connetti istanza dimostrativa**: vengono mostrati sei componenti sintetici, senza lettura di progetti reali. **Disconnetti demo** e il ritorno alla modalità reale svuotano sessione e componenti. Durante la verifica i comandi concorrenti e il cambio di modalità sono bloccati.

XML e ZIP sono predisposti come contratti e piani di esportazione (`manifest.xml`, `components.xml`, `diagnostics.xml`). I pulsanti restano disabilitati: non vengono creati file né archivi.

## Struttura

- `TiaAnalyzer.App`: View XAML, ViewModel osservabile, comandi asincroni e composizione dei servizi. Il code-behind contiene solo inizializzazione e ciclo di vita della finestra.
- `TiaAnalyzer.Core`: contratti diagnostici, sessione in sola lettura e componenti analizzati.
- `TiaAnalyzer.Openness`: probe metadati V21, adattatore reale non implementato, servizio demo.
- `TiaAnalyzer.Export`: contratti e piani futuri XML/ZIP, nessuna scrittura.
- `TiaAnalyzer.Checks`: controlli portabili dei sorgenti dei servizi e del ViewModel, senza framework di test aggiuntivi.

## Verifiche cloud

```bash
dotnet build TIA-Project-Analyzer.slnx -c Debug -p:Platform=x64
dotnet build TIA-Project-Analyzer.slnx -c Release -p:Platform=x64
dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj
```

I controlli portabili richiedono SDK .NET 10 ed eseguono i sorgenti collegati su runtime .NET 10. Coprono comandi, transizioni, errori del probe, stato concorrente, dati sintetici e contratti futuri; non sostituiscono la verifica del runtime Windows net48 o dei binding WPF. La compilazione cloud non certifica avvio grafico né integrazione Siemens. Vedere la checklist Windows per le verifiche ancora necessarie.
