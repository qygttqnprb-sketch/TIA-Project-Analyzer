# TIA Project Analyzer · V0.9

Applicazione Windows WPF, C# 7.3, .NET Framework 4.8, x64 per esportare e analizzare
in sola lettura il software PLC di progetti Siemens TIA Portal V21 già aperti.
La modalità demo usa esclusivamente dati sintetici.

## Funzioni

- Diagnostica DLL V21 e appartenenza al gruppo Openness nel token Windows.
- Rilevamento istanze e collegamento esplicito al PID; nessun Attach automatico.
- XML ricorsivi di OB, FB, FC, DB, UDT e tabelle tag, con cartelle, blocchi/tipi
  di sistema e Software/Safety Units accessibili.
- ZIP con domanda, XML originali, manifest JSON, SHA256, indice, report,
  inventario hardware, diagnostica e documentazione di ogni blocco/rete.
- Chiamate, dipendenze delle istanze DB, dichiarazioni/riferimenti dei simboli e grafo Graphviz.
- Ricerca variabili/indirizzi nell'ultimo snapshot e confronto tra due ZIP o manifest.
- Riferimenti incrociati Siemens con tipo di accesso, cache analisi, riuso XML opzionale
  con fingerprint e misure dei tempi; vedere [cache e prestazioni](docs/PERFORMANCE-CACHE.md).
- Traccia dei consensi SCL nel sottoinsieme supportato, ZIP mirato a un simbolo/blocco
  e confronto delle singole reti con integrità XML verificata; vedere [condizioni e confronto](docs/CONDITIONS-FOCUS-DIFF.md).
- Consensi booleani LAD/FBD, bobine Set/Reset e indice dei siti di scrittura tra blocchi:
  report con rete/UId, ambiti separati e copertura dichiarata; vedere [reti grafiche e scritture](docs/GRAPH-WRITERS.md).
- Apertura offline di ZIP/manifest con verifica SHA256, ricalcolo delle evidenze,
  ricerca e anteprime delle reti senza Attach; vedere [consultazione offline](docs/OFFLINE-SNAPSHOTS.md).
- Annullamento tra operazioni Siemens, esiti per oggetto, protezioni conservate,
  manifest/ZIP pubblicati soltanto dopo la scrittura completa. Nessun invio automatico.

## Build Windows

Servono Visual Studio con workload desktop .NET, targeting pack .NET Framework 4.8,
SDK compatibile con `.slnx` (verificato SDK 10.0.403) e piattaforma x64.

```powershell
dotnet build TIA-Project-Analyzer.slnx -c Debug -p:Platform=x64 -p:EnableSiemensOpenness=true
dotnet build TIA-Project-Analyzer.slnx -c Release -p:Platform=x64 -p:EnableSiemensOpenness=true
```

Base/Step7 vengono referenziate da `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`
con `Private=false`: nessuna DLL Siemens nel repository/output. Per F5 selezionare
`TiaAnalyzer.App` e aggiungere un `Directory.Build.props` locale, ignorato da Git:

```xml
<Project>
  <PropertyGroup><EnableSiemensOpenness>true</EnableSiemensOpenness></PropertyGroup>
</Project>
```

Senza la proprietà viene compilato il fallback: diagnostica/demo funzionano,
l'accesso Siemens è disabilitato. Dettagli in [OPENNESS-V21.md](docs/OPENNESS-V21.md).
Eseguibile Release: `TiaAnalyzer.App\bin\x64\Release\TiaAnalyzer.App.exe`.

## Uso

1. Verificare l'ambiente e il gruppo Openness. Se manca, seguire le
   [indicazioni IT](docs/OPENNESS-AUTHORIZATION.md), poi rifare l'accesso a Windows.
2. Aprire manualmente un progetto di prova in TIA V21 con PLC offline.
3. Rilevare, selezionare il PID, confermare e collegarsi. Rispondere al dialogo Siemens.
4. Scegliere la cartella, scrivere la domanda e premere **Crea ZIP per ChatGPT**.
5. Controllare report, esiti Failed/InterfaceOnly e limitazioni; allegare manualmente lo ZIP.
6. Cercare simboli oppure confrontare due esportazioni con **Sfoglia**.
7. Disconnettere: TIA e il progetto devono restare aperti.
8. Per consultare un pacchetto esistente, usare Apri snapshot offline nella ricerca:
   non è necessaria una sessione TIA; selezionare un blocco/rete per leggerne l'XML.

La demo segue lo stesso flusso e produce sei XML sintetici, marcati DEMO nel manifest.

## Test

```powershell
dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj
dotnet build TiaAnalyzer.WpfChecks/TiaAnalyzer.WpfChecks.csproj -c Debug -p:Platform=x64 -p:EnableSiemensOpenness=true
.\TiaAnalyzer.WpfChecks\bin\x64\Debug\net48\TiaAnalyzer.WpfChecks.exe
.\TiaAnalyzer.WpfChecks\bin\x64\Debug\net48\TiaAnalyzer.WpfChecks.exe --graph-only
.\TiaAnalyzer.WpfChecks\bin\x64\Debug\net48\TiaAnalyzer.WpfChecks.exe --offline-only
```

Il primo runner verifica backend simulati, export, ZIP, analisi XML e confronto su .NET 10.
Il secondo verifica WPF/net48 fuori schermo: binding, demo, ZIP, ricerca, confronto,
disconnessione e chiusura; salva immagini e dati sintetici in `artifacts/`.
Non cambia execution policy e non esegue Attach reale.

Per uno ZIP di esempio in una cartella nuova:

```powershell
dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj -- --demo-package artifacts/demo
```

## Stato e limiti

L'adapter reale compila contro Base/Step7 V21 installate; WPF/net48 è verificato in demo.
**Attach ed export reali restano da provare con un progetto di prova autorizzato aperto in TIA.**
Build e dati sintetici non verificano il contenuto di un PLC reale.

Le reti originali vengono conservate. Il testo SCL è ricostruito dai token, i candidati
testuali sono indicati come tali e le chiamate ambigue restano irrisolte. Non sono
disponibili valori runtime né uno snapshot atomico rispetto a modifiche in TIA.
Export HMI, allarmi, parametri azionamenti e analisi completa di sequenze/linguaggi
restano estensioni future. Le tracce SCL/LAD/FBD dichiarano il sottoinsieme modellato.
Timer, fronti, istruzioni grafiche non riconosciute e schemi diversi da FlgNet v4
restano parziali. Più siti di scrittura non dimostrano conflitti o priorità.

L'app non apre/salva progetti, non compila/scarica PLC, non rimuove protezioni,
non scrive DB/hardware e non esegue operazioni online. Windows/Siemens gestiscono
le autorizzazioni generali Openness.

Documentazione: [export PLC](docs/PLC-EXPORT.md), [ZIP](docs/CHATGPT-PACKAGE.md),
[analisi e confronto](docs/STATIC-ANALYSIS.md), [prove reali Windows](docs/WINDOWS-VALIDATION.md),
[esiti del 4 novembre 2026](docs/VALIDATION-2026-11-04.md).
