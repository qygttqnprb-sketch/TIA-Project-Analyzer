# V0.2 · adapter Siemens V21 (da compilare e verificare su Windows)

La V0.1 è stata compilata e avviata sul PC Windows dall'utente, con DLL Base/Step7 21.0.0.0 rilevate. Questo risultato non verifica la nuova connessione V0.2.

## Riferimenti e compilazione locale

`TiaAnalyzer.Openness` usa riferimenti tipizzati, senza dynamic o reflection per invocare API. `SiemensBackend.V21.cs` è isolato dal simbolo `SIEMENS_V21` ed escluso dalle build cloud. Nessuna DLL Siemens deve essere inclusa in Git. Le reference hanno `Private=false`: non vengono copiate nell'output.

Da Developer PowerShell di Visual Studio, con workload desktop .NET, targeting pack net48 e SDK .NET compatibile con slnx:

```powershell
MSBuild TIA-Project-Analyzer.slnx /restore /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /p:EnableSiemensOpenness=true
```

Il progetto aggiunge automaticamente:

- `Siemens.Engineering.Base.dll`
- `Siemens.Engineering.Step7.dll`

HintPath: `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`.

In Visual Studio usare un file locale ignorato `Directory.Build.props` alla radice della soluzione, se non si vuole passare il parametro a ogni build:

```xml
<Project>
  <PropertyGroup>
    <EnableSiemensOpenness>true</EnableSiemensOpenness>
  </PropertyGroup>
</Project>
```

Quindi ricompilare da Visual Studio. Il file non contiene segreti, ma resta locale per evitare di abilitare l'adapter sulle macchine senza Siemens. Il simbolo deve essere impostato tramite la proprietà, così si attivano anche reference e validazione dei percorsi. `SiemensPublicApiPath` può essere sovrascritto per la compilazione; il resolver e il probe runtime usano intenzionalmente il percorso V21 standard sopra indicato.

`App.OnStartup` registra un resolver limitato alle identità `Siemens.Engineering.*` richieste, con identità completa corrispondente e versione principale 21, dalla directory installata. Nessun caricamento da cartelle del progetto. Se mancano dipendenze, mostrare l'errore e verificare installazione e documentazione Siemens senza copiare binari.

Senza `EnableSiemensOpenness=true` viene compilato un adapter indisponibile: diagnostica e demo funzionano, mentre il rilevamento reale resta disabilitato. Una build cloud riuscita **non compila il codice Siemens**. La prima build Windows abilitata è obbligatoria: eventuali differenze nelle firme/API V21 devono essere risolte contro le DLL e l'help ufficiale installati, prima di considerare operativa la funzione.

## Superficie API utilizzata

Il file tipizzato usa la superficie Openness documentata per il collegamento a un'istanza esistente e la lettura del modello:

| Operazione | API utilizzata |
| --- | --- |
| Rilevamento | `TiaPortal.GetProcesses()` |
| Metadati istanza | `TiaPortalProcess.Id`, `Path`, `ProjectPath` |
| Collegamento richiesto dall'utente | `TiaPortalProcess.Attach()` |
| Progetti già aperti | `TiaPortal.Projects`, `Project.Name`, `Project.Path` |
| Dispositivi e gruppi | `Project.Devices`, `DeviceGroups`, `UngroupedDevicesGroup.Devices`, `DeviceUserGroup.Devices` / `Groups` |
| Elementi hardware | `Device.DeviceItems`, `DeviceItem.DeviceItems` |
| Identificazione software PLC | `DeviceItem.GetService<SoftwareContainer>()`, `SoftwareContainer.Software as PlcSoftware` |
| Rilascio del collegamento esistente | `Dispose()` del solo handle restituito da `Attach()` |

La versione visualizzata proviene dai metadati file dell'eseguibile (`FileVersionInfo`), non da una proprietà Version inventata sull'oggetto Siemens. Percorso/progetto possono non essere disponibili prima di Attach; dopo il collegamento i progetti aperti vengono enumerati. I dispositivi nei gruppi vengono attraversati e deduplicati; i moduli hardware sono attraversati ricorsivamente per trovare software STEP 7 dove presente. Nessun blocco PLC viene letto o modificato.

Le DLL e l'help V21 non sono disponibili nel cloud: la tabella descrive i punti che devono essere confrontati con l'API reference V21 locale e compilati su Windows. Non sono stati usati stub Siemens per simulare una compilazione dell'adapter reale.

## Autorizzazioni, errori e proprietà delle risorse

- L'avvio esegue solo il probe dei metadati. Rilevamento e collegamento sono comandi separati; nessuna selezione automatica, nessun Attach automatico.
- Per il reale occorrono PID selezionato e conferma esplicita del progetto di prova. La conferma si azzera a ogni selezione/rilevamento/cambio modalità.
- `Attach()` lascia a Siemens il dialogo standard di autorizzazione Openness. L'app non conferma dialoghi e non modifica gruppi o policy Windows. Verificare con l'amministratore l'appartenenza al gruppo locale Siemens TIA Openness e i requisiti dell'installazione V21; dopo variazioni dei gruppi può essere necessario un nuovo accesso Windows.
- Durante Attach la UI rimane responsiva ma blocca comandi concorrenti e cambio modalità. Se il dialogo è in attesa, rispondere in Siemens. Non viene implementato un timeout che abbandoni un Attach ancora in corso e perda la proprietà dell'handle.
- Rifiuto, istanza terminata, lettura fallita e API non disponibile vengono mostrati nello stato e nel log in memoria, limitato a 100 eventi. Nessun log viene scritto nel progetto o su disco.
- Tutte le chiamate Siemens, inclusi lettura e Dispose, sono serializzate su un unico thread STA. Gli oggetti Siemens non raggiungono il ViewModel: vengono restituiti solo DTO.
- In caso di errore di lettura viene rilasciato anche un collegamento parziale. Un errore di rilascio mantiene l'handle per un nuovo tentativo di disconnessione.
- Disconnessione e chiusura della dashboard rilasciano l'handle di una **istanza già esistente**. Non vengono mai creati TiaPortal, chiamati `Project.Close`, `Project.Open`, `Save`, compilatori, servizi online o terminati processi.
- Questo è un vincolo dell'adapter: non è una sandbox che limita le autorizzazioni generali di Openness. Verificare inizialmente con un progetto di prova.
