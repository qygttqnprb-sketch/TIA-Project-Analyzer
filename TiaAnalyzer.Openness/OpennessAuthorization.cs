using System;
using System.Security.Principal;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    public static class OpennessAuthorization
    {
        public const string GroupName = "Siemens TIA Openness";
        public const string Recovery = "Chiedere all'IT l'appartenenza al gruppo locale Siemens TIA Openness, " +
            "quindi disconnettersi completamente da Windows e accedere di nuovo. " +
            "Se il gruppo è già presente nel token, verificare il consenso nel dialogo Siemens e le policy aziendali.";

        public static EnvironmentCheck Inspect()
        {
#if NET5_0_OR_GREATER
            if (!OperatingSystem.IsWindows()) return Describe(null, "Verifica disponibile soltanto su Windows.");
#endif
            try
            {
                // Check the effective process token, including inherited membership. Merely
                // listing direct group members cannot establish the current logon's access.
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var group = new NTAccount(Environment.MachineName, GroupName);
                    var sid = (SecurityIdentifier)group.Translate(typeof(SecurityIdentifier));
                    return Describe(new WindowsPrincipal(identity).IsInRole(sid));
                }
            }
            catch (IdentityNotMappedException) { return Describe(null, "Gruppo locale non trovato: verificare l'installazione con l'IT."); }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException ||
                ex is PlatformNotSupportedException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
            {
                return Describe(null, "Impossibile verificare il token Windows: " + ex.Message);
            }
        }

        public static EnvironmentCheck Describe(bool? effectiveMembership, string detail = null)
        {
            return new EnvironmentCheck
            {
                Name = "Gruppo Windows / " + GroupName,
                Status = effectiveMembership == true ? "OK" : effectiveMembership == false ? "ERRORE" : "DA VERIFICARE",
                Detail = effectiveMembership == true
                    ? "Appartenenza attiva nel token del processo. Attach richiede anche il consenso Openness Siemens."
                    : (detail ?? "Gruppo assente dal token attuale (anche se aggiunto dopo l'accesso).") + " " + Recovery
            };
        }
    }
}
