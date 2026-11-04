# Pacchetto ChatGPT · V0.4

Collegare l'istanza, scegliere la cartella, scrivere la domanda e premere
**Crea ZIP per ChatGPT**. Il pulsante raccoglie uno snapshot nuovo, genera la
documentazione e pubblica lo ZIP solo dopo la chiusura corretta dell'archivio.
Lo ZIP è affiancato dalla cartella XML ispezionabile. Nessun invio automatico.

Il pacchetto comprende:

- `README.md`: istruzioni per leggere il materiale e distinguere codice da ipotesi.
- `QUESTION.md`: problema descritto dall'utente.
- `index.md`: indice dei blocchi con collegamenti ai file XML originali.
- `report.md`: conteggi, errori di raccolta e limitazioni.
- `manifest.json`: provenienza DEMO/reale, scope PLC, SHA256, dimensioni ed esiti.
- `components.json`: inventario dei componenti hardware/software letti.
- `diagnostics.json`: controlli dell'ambiente e log della sessione.
- `plc/`: blocchi, UDT e tabelle variabili XML, senza riscrittura dei contenuti.

Allegare manualmente lo ZIP a ChatGPT e ripetere o precisare la domanda. Chiedere
sempre riferimenti ai blocchi/reti e quali segnali controllare per confermare
un'ipotesi. Uno snapshot offline non identifica da solo lo stato attuale del PLC.
`Failed`, `InterfaceOnly` e Issues indicano dati mancanti, anche quando lo ZIP
è stato creato correttamente. Rivedere il contenuto prima della condivisione.

La compressione usa solo i file dichiarati: altri file nella cartella non vengono
inclusi. Il file `.partial` appartiene alla sola operazione in corso; un errore o
un annullamento non pubblica uno ZIP troncato e non sovrascrive ZIP precedenti.

Per generare un esempio completamente sintetico:

```powershell
dotnet run --project TiaAnalyzer.Checks/TiaAnalyzer.Checks.csproj -- --demo-package artifacts/demo
```

La cartella di esempio deve essere nuova. Gli artefatti locali sono ignorati da Git.
