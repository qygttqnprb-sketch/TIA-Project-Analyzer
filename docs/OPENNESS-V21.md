# Adapter Siemens Openness V21

Il codice tipizzato è compilato con `EnableSiemensOpenness=true`. Richiede Base/Step7
locali in `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`.
Reference `Private=false`; il target MSBuild controlla entrambe le DLL.
`SiemensPublicApiPath` cambia il percorso di compilazione, mentre probe/resolver
runtime usano il percorso standard. Il resolver accetta solo identità complete
Siemens.Engineering.* corrispondenti, versione 21, dall'installazione.

## API utilizzate

- `TiaPortal.GetProcesses`, metadati del processo e `TiaPortalProcess.Attach`.
- `TiaPortal.Projects`, dispositivi/gruppi, hardware e `SoftwareContainer`.
- `PlcSoftware.BlockGroup`, `TypeGroup`, `TagTableGroup`, gruppi utente/di sistema.
- `PlcUnitProvider.UnitGroup.Units` e `SafetyUnits` con blocchi/tipi/tag.
- `PlcBlock.Export`, `PlcType.Export`, `PlcTagTable.Export`: `ExportOptions.WithDefaults`.
- `FingerprintProvider.GetFingerprints` e `CrossReferenceService.GetCrossReferences`:
  servizi di lettura opzionali, con esito esplicito e fallback all'XML quando non disponibili.
- `Dispose` del solo handle Attach, senza chiudere progetti o processi TIA.

`SiemensBackend.V21.cs` gestisce connessione/inventario; `SiemensExport.V21.cs` l'export.
Tutte le API, inclusi Export/Dispose, sono serializzate sullo stesso worker STA.
Solo DTO/file raggiungono la UI. Nessun TiaPortal creato, import, salvataggio,
apertura progetto, compilazione, sblocco o accesso online.

## Autorizzazioni e sessioni

Il probe legge metadati e token senza collegarsi. Il gruppo locale Siemens TIA Openness
deve essere attivo nel token, anche attraverso gruppi ereditati. Dopo l'aggiunta serve
un nuovo accesso Windows completo. Il dialogo Openness è un controllo separato.
Vedere [autorizzazione](OPENNESS-AUTHORIZATION.md).

La UI richiede PID selezionato e consenso al progetto di prova. Durante le chiamate
blocca selezione, cambio modalità e chiusura. L'annullamento viene osservato tra
chiamate senza abbandonare l'handle. Un errore di Dispose mantiene la sessione per retry.

La compilazione contro DLL V21 installate verifica le firme API, senza stub.
Non verifica Attach, autorizzazioni progetto o XML reale: seguire
[WINDOWS-VALIDATION.md](WINDOWS-VALIDATION.md) dopo l'intervento IT.
