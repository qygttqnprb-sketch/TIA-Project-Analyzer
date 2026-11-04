using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using TiaAnalyzer.App.Infrastructure;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

namespace TiaAnalyzer.App.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private readonly IEnvironmentProbe realProbe, demoProbe;
        private readonly ITiaConnectionService realConnection, demoConnection;
        private readonly DiagnosticLog log = new DiagnosticLog();
        private bool demo, busy, ready, connected, consent;
        private TiaInstance selected;
        private IList<EnvironmentCheck> checks = new List<EnvironmentCheck>();
        private IReadOnlyList<TiaInstance> instances = new List<TiaInstance>();
        private IReadOnlyList<ProjectComponent> components = new List<ProjectComponent>();
        private string environmentStatus = "Controllo in attesa", lastCheck = "Nessun controllo eseguito", sessionStatus = "Disconnesso";
        private string exportRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TiaAnalyzerExports");
        private string exportStatus = "Collegare un'istanza per esportare XML e manifest.", lastExportDirectory;
        private CancellationTokenSource exportCancellation;
        private ITiaConnectionService Service { get { return demo ? demoConnection : realConnection; } }
        public MainViewModel() : this(new SiemensEnvironmentProbe(), new MockEnvironmentProbe(),
            new TiaConnectionService(new SiemensBackend()), new TiaConnectionService(new DemoBackend())) { }
        public MainViewModel(IEnvironmentProbe realProbe, IEnvironmentProbe demoProbe, ITiaConnectionService realConnection, ITiaConnectionService demoConnection)
        {
            this.realProbe = realProbe; this.demoProbe = demoProbe;
            this.realConnection = realConnection; this.demoConnection = demoConnection;
            CheckCommand = Command(CheckAsync, () => !busy);
            ScanCommand = Command(ScanAsync, () => !busy && !connected && ready && Service.IsAvailable);
            ConnectCommand = Command(ConnectAsync, () => !busy && !connected && ready && selected != null && Service.IsAvailable && (demo || consent));
            DisconnectCommand = Command(DisconnectAsync, () => !busy && connected);
            ExportXmlCommand = Command(ExportXmlAsync, () => !busy && connected);
            CancelExportCommand = new AsyncCommand(() => { exportCancellation?.Cancel(); return Task.CompletedTask; },
                () => exportCancellation != null && !exportCancellation.IsCancellationRequested, ReportError);
        }
        private AsyncCommand Command(Func<Task> action, Func<bool> canRun)
        {
            return new AsyncCommand(async () =>
            {
                SetBusy(true);
                try { await action(); }
                catch (Exception ex) { ReportError(ex); }
                finally { SetBusy(false); }
            }, canRun, ReportError);
        }
        public AsyncCommand CheckCommand { get; }
        public AsyncCommand ScanCommand { get; }
        public AsyncCommand ConnectCommand { get; }
        public AsyncCommand DisconnectCommand { get; }
        public AsyncCommand ExportXmlCommand { get; }
        public AsyncCommand CancelExportCommand { get; }
        public bool IsDemo
        {
            get { return demo; }
            set
            {
                if (demo == value || busy) return;
                ChangeModeCommand(value);
            }
        }
        private async void ChangeModeCommand(bool value)
        {
            SetBusy(true);
            try
            {
                await Service.DisconnectAsync();
                ClearSession(); ClearInstances();
                demo = value; ready = false; consent = false;
                Notify(nameof(IsDemo)); Notify(nameof(LibraryPath)); Notify(nameof(ConnectLabel)); Notify(nameof(ModeLabel)); Notify(nameof(RealConsent));
                await CheckAsync();
            }
            catch (Exception ex) { ReportError(ex); Notify(nameof(IsDemo)); }
            finally { SetBusy(false); }
        }
        public bool RealConsent { get { return consent; } set { if (busy) return; consent = value; Notify(); RefreshCommands(); } }
        public bool IsBusy { get { return busy; } }
        public bool CanChangeMode { get { return !busy; } }
        public bool CanSelectInstance { get { return !busy && !connected; } }
        public TiaInstance SelectedInstance
        {
            get { return selected; }
            set { if (!CanSelectInstance) return; selected = value; consent = false; Notify(); Notify(nameof(RealConsent)); RefreshCommands(); }
        }
        public IReadOnlyList<TiaInstance> Instances { get { return instances; } }
        public IReadOnlyList<string> LogEntries { get { return log.Snapshot(); } }
        public string AdapterStatus { get { return realConnection.IsAvailable ? "Adapter V21 incluso · connessione da verificare su Windows" : "Adapter reale escluso dalla build · abilitare EnableSiemensOpenness su Windows"; } }
        public string ModeLabel { get { return demo ? "DEMO / DATI SINTETICI" : "REALE / SOLA LETTURA"; } }
        public string LibraryPath { get { return demo ? "DEMO • nessuna DLL Siemens utilizzata" : SiemensEnvironmentProbe.LibraryPath; } }
        public string ConnectLabel { get { return demo ? "Connetti istanza dimostrativa" : "Collega istanza selezionata"; } }
        public string EnvironmentStatus { get { return environmentStatus; } }
        public string LastCheck { get { return lastCheck; } }
        public string SessionStatus { get { return sessionStatus; } }
        public string ExportStatus { get { return exportStatus; } }
        public string ExportRoot { get { return exportRoot; } set { if (busy) return; exportRoot = value; Notify(); } }
        public string LastExportDirectory { get { return lastExportDirectory; } }
        public IList<EnvironmentCheck> Checks { get { return checks; } }
        public IReadOnlyList<ProjectComponent> Components { get { return components; } }
        public string ComponentSummary { get { return components.Count + (demo ? " componenti sintetici" : " elementi letti dai soli progetti già aperti"); } }
        public Task InitializeAsync() { return CheckCommand.ExecuteAsync(); }
        private async Task CheckAsync()
        {
            ready = false;
            environmentStatus = "Verifica in corso…"; Notify(nameof(EnvironmentStatus));
            EnvironmentReport report;
            try { report = await Task.Run(() => (demo ? demoProbe : realProbe).Inspect()); }
            catch (Exception ex)
            {
                environmentStatus = "Verifica non riuscita";
                checks = new[] { new EnvironmentCheck { Name = "Diagnostica", Status = "ERRORE", Detail = ex.Message } };
                Notify(nameof(EnvironmentStatus)); Notify(nameof(Checks));
                throw;
            }
            checks = report.Checks; ready = report.Available;
            environmentStatus = demo ? "DEMO · ambiente simulato" : ready ? "Librerie V21 rilevate · integrazione non verificata" : "Ambiente incompleto · consultare i controlli";
            lastCheck = "Ultimo controllo: " + DateTime.Now.ToString("HH:mm:ss");
            Notify(nameof(Checks)); Notify(nameof(EnvironmentStatus)); Notify(nameof(LastCheck));
            Log("Diagnostica completata. Nessun collegamento automatico.");
        }
        private async Task ScanAsync()
        {
            ClearInstances();
            instances = await Service.DiscoverAsync();
            Notify(nameof(Instances));
            sessionStatus = instances.Count == 0 ? "Nessuna istanza TIA disponibile" : "Selezionare manualmente una delle " + instances.Count + " istanze";
            Notify(nameof(SessionStatus)); Log(sessionStatus);
        }
        private async Task ConnectAsync()
        {
            var target = selected;
            sessionStatus = "Attach al PID " + target.Id + " · attendere l'eventuale autorizzazione Siemens; rispondere nel dialogo TIA";
            Notify(nameof(SessionStatus)); Log("Collegamento esplicito al PID " + target.Id);
            components = await Service.ConnectAsync(target);
            connected = true;
            sessionStatus = (demo ? "MOCK connesso" : "Collegato in sola lettura") + " · PID " + target.Id;
            NotifySession(); Log(sessionStatus);
        }
        private async Task DisconnectAsync()
        {
            await Service.DisconnectAsync();
            ClearSession(); Log("Risorse Attach rilasciate; nessuna richiesta di chiusura TIA.");
        }
        private async Task ExportXmlAsync()
        {
            var root = Path.GetFullPath(exportRoot);
            var directory = Path.Combine(root, (demo ? "DEMO_" : "TIA_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (var cancellation = new CancellationTokenSource())
            {
                exportCancellation = cancellation; RefreshCommands();
                try
                {
                    var progress = new Progress<ExportProgress>(p =>
                    {
                        if (exportCancellation == cancellation) SetExportStatus(p.Completed + " oggetti · " + p.Message);
                    });
                    SetExportStatus("Esportazione PLC in corso…");
                    var snapshot = await Service.ExportPlcAsync(directory, progress, cancellation.Token);
                    await Task.Run(() => SnapshotStore.Complete(directory, snapshot, cancellation.Token));
                    lastExportDirectory = directory; Notify(nameof(LastExportDirectory));
                    SetExportStatus((demo ? "DEMO · " : "") + snapshot.Artifacts.Count + " oggetti; " + snapshot.Issues.Count + " problemi. Cartella: " + directory);
                    Log(exportStatus);
                }
                catch (OperationCanceledException) { SetExportStatus("Esportazione annullata. Cartella incompleta: " + directory); Log(exportStatus); }
                finally { exportCancellation = null; RefreshCommands(); }
            }
        }
        private void SetExportStatus(string message) { exportStatus = message; Notify(nameof(ExportStatus)); }
        public async Task<bool> CloseAsync()
        {
            if (busy) { Log("Operazione in corso: completare l'eventuale autorizzazione Siemens prima di chiudere."); return false; }
            SetBusy(true);
            try
            {
                await realConnection.DisconnectAsync();
                await demoConnection.DisconnectAsync();
                ClearSession();
                await realConnection.ShutdownAsync();
                await demoConnection.ShutdownAsync();
                return true;
            }
            catch (Exception ex) { ReportError(ex); return false; }
            finally { SetBusy(false); }
        }
        private void ReportError(Exception ex)
        {
            connected = Service.HasSession;
            Notify(nameof(CanSelectInstance));
            var connectionError = ex as TiaConnectionException;
            sessionStatus = (connectionError == null ? "Errore" : connectionError.Failure.ToString()) + ": " + ex.Message;
            Notify(nameof(SessionStatus));
            Log(sessionStatus);
            // Never clear a still-owned handle on a failed Disconnect. Permit retry.
        }
        private void ClearInstances()
        {
            instances = new List<TiaInstance>(); selected = null; consent = false;
            Notify(nameof(Instances)); Notify(nameof(SelectedInstance)); Notify(nameof(RealConsent));
        }
        private void ClearSession()
        {
            connected = false; components = new List<ProjectComponent>(); sessionStatus = "Disconnesso";
            lastExportDirectory = null; Notify(nameof(LastExportDirectory));
            SetExportStatus("Collegare un'istanza per esportare XML e manifest.");
            NotifySession();
        }
        private void NotifySession()
        {
            Notify(nameof(SessionStatus)); Notify(nameof(Components)); Notify(nameof(ComponentSummary)); Notify(nameof(CanSelectInstance)); RefreshCommands();
        }
        private void Log(string message) { log.Add(message); Notify(nameof(LogEntries)); }
        private void SetBusy(bool value)
        {
            busy = value; Notify(nameof(IsBusy)); Notify(nameof(CanChangeMode)); Notify(nameof(CanSelectInstance)); RefreshCommands();
        }
        private void RefreshCommands()
        {
            CheckCommand?.Refresh(); ScanCommand?.Refresh(); ConnectCommand?.Refresh(); DisconnectCommand?.Refresh();
            ExportXmlCommand?.Refresh(); CancelExportCommand?.Refresh();
        }
    }
}
