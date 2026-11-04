# Autorizzazione Openness V21

`AuthorizationDenied` può dipendere sia dal gruppo Windows sia dal consenso Openness.
L'app mostra l'appartenenza **nel token del processo corrente**, comprese le appartenenze
ereditate: un elenco dei soli membri diretti del gruppo non è una verifica sufficiente.
Il controllo non modifica gruppi, registro, firewall o policy.

## Verifica dal proprio account

```powershell
whoami /groups
Get-LocalGroupMember -Group 'Siemens TIA Openness'
```

`Siemens TIA Engineer` non sostituisce `Siemens TIA Openness`. Se il secondo gruppo
non è nel token, chiedere all'IT di aggiungere l'account al gruppo locale autorizzato.
**Disconnettersi completamente da Windows e accedere di nuovo**: riavviare soltanto
l'app non aggiorna il token della sessione Windows.

Dopo il nuovo accesso, avviare la build con `EnableSiemensOpenness=true`, verificare
il controllo del gruppo, aprire un progetto di prova offline, rilevare e selezionare
l'istanza e premere Collega. Rispondere manualmente al dialogo Siemens.
Il gruppo abilita in generale l'uso di Openness; non dimostra che Attach sia riuscito.

Se il gruppo è attivo e il rifiuto persiste, registrare il messaggio completo,
verificare con l'IT le policy e il dialogo Openness. Non aggirare le impostazioni
aziendali né aggiungere automaticamente voci all'AllowList.

Fonti ufficiali V21:

- [Appartenenza e nuovo accesso Windows](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/basics/installation/adding-users-to-the-siemens-tia-openness-user-group)
- [Dialogo e autorizzazione Openness](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/general-functions/tia-portal-openness-firewall)
