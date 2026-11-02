using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TiaAnalyzer.App.Infrastructure;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

namespace TiaAnalyzer.App.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private readonly IEnvironmentProbe realProbe;
        private readonly IEnvironmentProbe demoProbe;
        private readonly ITiaConnectionService realConnection;
        private readonly ITiaConnectionService demoConnection;
        private bool demo;
        private bool busy;
        private bool demoReady;
        private IReadOnlyTiaSession session;
        private IList<EnvironmentCheck> checks = new List<EnvironmentCheck>();
        private IReadOnlyList<ProjectComponent> components = new List<ProjectComponent>();
        private string environmentStatus = "Controllo in attesa";
        private string lastCheck = "Nessun controllo eseguito";
        private string sessionStatus = "Disconnesso";

        public MainViewModel() : this(new SiemensEnvironmentProbe(), new MockEnvironmentProbe(),
            new SiemensConnectionService(), new DemoConnectionService()) { }

        public MainViewModel(IEnvironmentProbe realProbe, IEnvironmentProbe demoProbe,
            ITiaConnectionService realConnection, ITiaConnectionService demoConnection)
        {
            this.realProbe = realProbe;
            this.demoProbe = demoProbe;
            this.realConnection = realConnection;
            this.demoConnection = demoConnection;
            CheckCommand = new AsyncCommand(CheckAsync, () => !IsBusy, ShowFailure);
            ConnectCommand = new RelayCommand(Connect, () => !IsBusy && IsDemo && demoReady && demoConnection.CanConnect && session == null);
            DisconnectCommand = new RelayCommand(ResetSession, () => !IsBusy && session != null);
        }

        public AsyncCommand CheckCommand { get; }
        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public bool IsDemo
        {
            get { return demo; }
            set
            {
                if (demo == value || IsBusy) return;
                demo = value;
                demoReady = false;
                ResetSession();
                Notify(); Notify(nameof(LibraryPath)); Notify(nameof(ConnectLabel)); Notify(nameof(ModeLabel));
                CheckCommand.Execute(null);
            }
        }
        public bool IsBusy { get { return busy; } }
        public bool CanChangeMode { get { return !busy; } }
        public string ModeLabel { get { return IsDemo ? "DEMO / DATI SINTETICI" : "REALE / NON CONNESSO"; } }
        public string LibraryPath { get { return IsDemo ? "DEMO • nessuna DLL Siemens utilizzata" : SiemensEnvironmentProbe.LibraryPath; } }
        public string ConnectLabel { get { return IsDemo ? "Connetti istanza dimostrativa" : "Connessione reale · futura versione"; } }
        public string EnvironmentStatus { get { return environmentStatus; } }
        public string LastCheck { get { return lastCheck; } }
        public string SessionStatus { get { return sessionStatus; } }
        public string ExportStatus { get { return ExportAvailability.Description; } }
        public IList<EnvironmentCheck> Checks { get { return checks; } }
        public IReadOnlyList<ProjectComponent> Components { get { return components; } }
        public string ComponentSummary { get { return components.Count == 0 ? "Nessun componente acquisito. Connetti la demo per esplorare i dati sintetici." : components.Count + " componenti sintetici · nessun progetto TIA letto"; } }

        public Task InitializeAsync() { return CheckCommand.ExecuteAsync(); }
        private async Task CheckAsync()
        {
            busy = true;
            demoReady = false;
            Notify(nameof(IsBusy)); Notify(nameof(CanChangeMode)); RefreshCommands();
            environmentStatus = "Verifica in corso…";
            Notify(nameof(EnvironmentStatus));
            try
            {
                var probe = IsDemo ? demoProbe : realProbe;
                var report = await Task.Run(() => probe.Inspect());
                checks = report.Checks;
                demoReady = IsDemo && report.Available;
                environmentStatus = IsDemo ? (report.Available ? "DEMO · ambiente simulato" : "DEMO · verifica incompleta") : report.Available
                    ? "Librerie V21 rilevate · integrazione Siemens non verificata"
                    : "Ambiente incompleto · integrazione Siemens non verificata";
                lastCheck = "Ultimo controllo: " + DateTime.Now.ToString("HH:mm:ss");
                Notify(nameof(Checks)); Notify(nameof(EnvironmentStatus)); Notify(nameof(LastCheck));
            }
            finally
            {
                busy = false;
                Notify(nameof(IsBusy)); Notify(nameof(CanChangeMode)); RefreshCommands();
            }
        }
        private void ShowFailure(Exception ex)
        {
            demoReady = false;
            environmentStatus = "Verifica non riuscita";
            checks = new[] { new EnvironmentCheck { Name = "Diagnostica", Status = "ERRORE", Detail = ex.Message } };
            Notify(nameof(EnvironmentStatus)); Notify(nameof(Checks)); RefreshCommands();
        }
        private void Connect()
        {
            try
            {
                // The real adapter remains deliberately unavailable in V0.1.
                var connection = IsDemo ? demoConnection : realConnection;
                session = connection.Connect();
                components = connection.GetComponents(session);
                sessionStatus = session.Description;
                NotifySession();
            }
            catch (Exception ex)
            {
                ResetSession();
                sessionStatus = "Connessione non riuscita: " + ex.Message;
                Notify(nameof(SessionStatus));
            }
        }
        private void ResetSession()
        {
            session = null;
            components = new List<ProjectComponent>();
            sessionStatus = "Disconnesso";
            NotifySession();
        }
        private void NotifySession()
        {
            Notify(nameof(SessionStatus)); Notify(nameof(Components)); Notify(nameof(ComponentSummary)); RefreshCommands();
        }
        private void RefreshCommands()
        {
            CheckCommand.Refresh(); ConnectCommand.Refresh(); DisconnectCommand.Refresh();
        }
    }
}
