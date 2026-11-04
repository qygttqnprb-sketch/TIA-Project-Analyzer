using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaAnalyzer.App.ViewModels;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

internal static class Program
{
    private static int passed;
    private static readonly List<ITiaConnectionService> services = new List<ITiaConnectionService>();
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name); passed++;
    }
    private static TiaConnectionService Service(ITiaBackend backend)
    {
        var service = new TiaConnectionService(backend); services.Add(service); return service;
    }
    private static async Task Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--demo-package") { await PackageChecks.GenerateDemo(args[1]); return; }
        try
        {
            await AdapterTests();
            await ViewModelTests();
            MetadataTests();
            await ExportChecks.Run(Check);
            await PackageChecks.Run(Check);
            Console.WriteLine(passed + " checks passed; simulated adapters/source-level .NET 10 only. Siemens V21 and WPF not validated.");
        }
        finally { foreach (var service in services) await service.ShutdownAsync(); }
    }
    private static async Task AdapterTests()
    {
        var backend = new FakeBackend();
        var service = Service(backend);
        backend.Instances.Clear();
        Check((await service.DiscoverAsync()).Count == 0, "no TIA instances");
        await Expect(ConnectionFailure.InstanceUnavailable, () => service.ConnectAsync(null), "attach requires discovered instance");
        backend.Instances.Add(Instance(21)); backend.Instances.Add(Instance(22));
        var list = await service.DiscoverAsync();
        Check(list.Count == 2 && list[0].Id != list[1].Id, "multiple selectable instances");
        await Expect(ConnectionFailure.InstanceUnavailable, () => service.ConnectAsync(Instance(21)), "reject instance not from discovery snapshot");
        var rows = await service.ConnectAsync(list[1]);
        Check(backend.AttachedId == 22 && service.HasSession && rows.Count == 2, "selected PID attached and project/device snapshot read");
        bool duplicateBlocked = false;
        try { await service.ConnectAsync(list[0]); } catch (InvalidOperationException) { duplicateBlocked = true; }
        Check(duplicateBlocked && backend.AttachCalls == 1, "duplicate attach blocked without leaking handle");
        await service.DisconnectAsync(); await service.DisconnectAsync();
        Check(!service.HasSession && backend.Released == 1, "disconnect idempotent and owned handle released exactly once");
        Check(backend.ThreadIds.Distinct().Count() == 1, "discover, attach, read and dispose use same worker");
        backend.AttachError = new TiaConnectionException(ConnectionFailure.AuthorizationDenied, "Denied by user");
        await Expect(ConnectionFailure.AuthorizationDenied, () => service.ConnectAsync(list[0]), "authorization refusal propagated");
        Check(!service.HasSession, "refused attach leaves no session");
        backend.AttachError = null;
        backend.ReadError = new TiaConnectionException(ConnectionFailure.ReadFailed, "Instance closed during read");
        await Expect(ConnectionFailure.ReadFailed, () => service.ConnectAsync(list[0]), "read error propagated");
        Check(!service.HasSession && backend.Released == 2, "read failure releases partial attachment");
        backend.ReadError = null;
        await service.ConnectAsync(list[0]);
        backend.ReleaseError = true;
        bool releaseFailed = false;
        try { await service.DisconnectAsync(); } catch (IOException) { releaseFailed = true; }
        Check(releaseFailed && service.HasSession, "failed release retains handle for retry");
        backend.ReleaseError = false;
        await service.DisconnectAsync();
        Check(!service.HasSession, "disconnect retry succeeds");
        backend.ReadError = new TiaConnectionException(ConnectionFailure.ReadFailed, "Read failed"); backend.ReleaseError = true;
        bool aggregate = false;
        try { await service.ConnectAsync(list[0]); } catch (AggregateException) { aggregate = true; }
        Check(aggregate && service.HasSession, "read plus cleanup errors retain retryable ownership");
        backend.ReleaseError = false; backend.ReadError = null;
        await service.DisconnectAsync();
        backend.EmptyProject = true;
        Check((await service.ConnectAsync(list[0])).Count == 0 && service.HasSession, "attached instance without open projects yields empty snapshot");
        await service.DisconnectAsync(); backend.EmptyProject = false;
        backend.AttachError = new TiaConnectionException(ConnectionFailure.InstanceUnavailable, "Process exited");
        await Expect(ConnectionFailure.InstanceUnavailable, () => service.ConnectAsync(list[0]), "instance closed between discovery and attach");
        backend.AttachError = null;
        backend.DiscoveryError = new IOException("Synthetic discovery error");
        bool scanFailed = false;
        try { await service.DiscoverAsync(); } catch (IOException) { scanFailed = true; }
        Check(scanFailed, "discovery errors propagated");
        backend.DiscoveryError = null;
        await Expect(ConnectionFailure.InstanceUnavailable, () => service.ConnectAsync(list[0]), "failed refresh invalidates old discovery");
        await Task.WhenAll(service.DiscoverAsync(), service.DiscoverAsync());
        Check(backend.ThreadIds.Distinct().Count() == 1, "concurrent requests serialized on same worker");
        var unavailable = Service(new SiemensBackend());
        Check(!unavailable.IsAvailable, "cloud build clearly excludes Siemens adapter");
        await Expect(ConnectionFailure.ApiUnavailable, () => unavailable.DiscoverAsync(), "missing API diagnosed explicitly");
    }
    private static async Task ViewModelTests()
    {
        var backend = new FakeBackend();
        var real = Service(backend);
        var demo = Service(new DemoBackend());
        var probe = new Probe();
        var vm = new MainViewModel(probe, new MockEnvironmentProbe(), real, demo);
        await vm.InitializeAsync();
        Check(backend.Scans == 0 && backend.AttachCalls == 0, "startup performs metadata check only; no automatic discovery/attach");
        await vm.ScanCommand.ExecuteAsync();
        Check(vm.Instances.Count == 2 && vm.SelectedInstance == null && !vm.ConnectCommand.CanExecute(null), "scan never auto-selects instance");
        vm.SelectedInstance = vm.Instances[1];
        Check(!vm.ConnectCommand.CanExecute(null), "real attach requires explicit test-project consent");
        vm.RealConsent = true;
        await vm.ConnectCommand.ExecuteAsync();
        Check(vm.Components.Count == 2 && backend.AttachedId == 22 && vm.DisconnectCommand.CanExecute(null), "real workflow with simulated successful attach");
        Check(!vm.ScanCommand.CanExecute(null) && !vm.ConnectCommand.CanExecute(null), "connected state blocks scan and duplicate attach");
        await vm.DisconnectCommand.ExecuteAsync();
        Check(vm.Components.Count == 0 && vm.SessionStatus == "Disconnesso" && !real.HasSession, "VM disconnect clears dashboard without terminating portal");
        backend.AttachError = new TiaConnectionException(ConnectionFailure.AuthorizationDenied, "User refused");
        await vm.ConnectCommand.ExecuteAsync();
        Check(vm.SessionStatus.Contains("AuthorizationDenied") && !vm.IsBusy && vm.LogEntries.Any(x => x.Contains("AuthorizationDenied")), "authorization denial shown and logged; UI recovers");
        backend.AttachError = null;
        backend.Instances.Clear();
        await vm.ScanCommand.ExecuteAsync();
        Check(vm.Instances.Count == 0 && vm.SelectedInstance == null && vm.SessionStatus.Contains("Nessuna istanza"), "empty refresh clears selection and disables attach");
        backend.Instances.Add(Instance(23));
        await vm.ScanCommand.ExecuteAsync(); vm.SelectedInstance = vm.Instances[0]; vm.RealConsent = true;
        await vm.ConnectCommand.ExecuteAsync();
        backend.ReleaseError = true;
        await vm.DisconnectCommand.ExecuteAsync();
        Check(vm.DisconnectCommand.CanExecute(null) && real.HasSession, "VM release failure remains retryable");
        backend.ReleaseError = false;
        await SwitchMode(vm, true);
        Check(!real.HasSession && vm.Components.Count == 0 && vm.Instances.Count == 0 && !vm.RealConsent, "mode change detaches and clears real snapshot/consent");
        await vm.ScanCommand.ExecuteAsync();
        vm.SelectedInstance = vm.Instances[0];
        await vm.ConnectCommand.ExecuteAsync();
        Check(vm.Components.Count == 6 && vm.Components.All(x => x.Status == "DEMO") && vm.SessionStatus.StartsWith("MOCK"), "demo remains independent with synthetic data");
        var vmExport = Path.Combine(Path.GetTempPath(), "tia-vm-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            vm.ExportRoot = vmExport; vm.Question = "Motore fermo";
            await vm.ExportZipCommand.ExecuteAsync();
            Check(File.Exists(vm.LastZipPath) && File.Exists(Path.Combine(vm.LastExportDirectory, "manifest.json")) &&
                !vm.IsBusy && demo.HasSession, "VM one-click ZIP workflow completes and keeps session attached");
        }
        finally { if (Directory.Exists(vmExport)) Directory.Delete(vmExport, true); }
        var attachCount = backend.AttachCalls;
        await SwitchMode(vm, false);
        Check(backend.AttachCalls == attachCount && vm.Components.Count == 0 && !demo.HasSession, "switch from demo never auto-attaches real portal");
        probe.Fail = true;
        await vm.CheckCommand.ExecuteAsync();
        Check(!vm.ScanCommand.CanExecute(null) && !vm.IsBusy && vm.SessionStatus.Contains("Synthetic probe"), "probe failure disables real discovery and recovers busy state");
        probe.Fail = false;
        await vm.CheckCommand.ExecuteAsync();
        await vm.ScanCommand.ExecuteAsync(); vm.SelectedInstance = vm.Instances[0]; vm.RealConsent = true;
        backend.AttachGate = new ManualResetEventSlim(false);
        var connecting = vm.ConnectCommand.ExecuteAsync();
        Check(vm.IsBusy && !vm.CanChangeMode && !vm.CanSelectInstance, "attach in progress freezes selection and mode");
        Check(!await vm.CloseAsync(), "window close waits for outstanding authorization/attach");
        backend.AttachGate.Set(); await connecting;
        Check(await vm.CloseAsync() && !real.HasSession && vm.Components.Count == 0, "window shutdown releases handles and workers");
        services.Remove(real); services.Remove(demo);
    }
    private static void MetadataTests()
    {
        var temp = Path.Combine(Path.GetTempPath(), "tia-v02-" + Guid.NewGuid());
        try
        {
            var report = new SiemensEnvironmentProbe(temp).Inspect();
            Check(!report.Available && report.Checks.Count == 7, "missing Siemens directory diagnosed");
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "Siemens.Engineering.Base.dll"), "invalid");
            File.Copy(typeof(Program).Assembly.Location, Path.Combine(temp, "Siemens.Engineering.Step7.dll"));
            report = new SiemensEnvironmentProbe(temp).Inspect();
            Check(!report.Available && report.Checks[2].Status == "ERRORE" && report.Checks[3].Status == "ERRORE", "invalid metadata and wrong identity rejected");
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        var log = new DiagnosticLog(); for (int i = 0; i < 120; i++) log.Add("event " + i);
        Check(log.Snapshot().Count == 100, "diagnostic log bounded in memory");
        Check(OpennessAuthorization.Describe(false).Status == "ERRORE" &&
            OpennessAuthorization.Describe(false).Detail.Contains("disconnettersi completamente"), "missing token membership explains full Windows sign-out");
        Check(OpennessAuthorization.Describe(true).Detail.Contains("consenso"), "membership does not imply Siemens authorization");
        Check(OpennessAuthorization.Describe(null).Status == "DA VERIFICARE", "unknown authorization is not reported as denied or granted");
    }
    private static async Task Expect(ConnectionFailure failure, Func<Task> action, string name)
    {
        try { await action(); throw new Exception("Expected " + failure); }
        catch (TiaConnectionException ex) { Check(ex.Failure == failure, name); }
    }
    private static async Task SwitchMode(MainViewModel vm, bool demo)
    {
        vm.IsDemo = demo;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.IsBusy)
        {
            if (DateTime.UtcNow > deadline) throw new Exception("Mode change timeout");
            await Task.Delay(1);
        }
        Check(vm.IsDemo == demo, "requested mode change completed");
    }
    private static TiaInstance Instance(int id)
    {
        return new TiaInstance { Id = id, Version = "21.0 (simulated)", ExecutablePath = "synthetic.exe", ProjectPath = "test-project" };
    }
    private sealed class Probe : IEnvironmentProbe
    {
        public bool Fail;
        public EnvironmentReport Inspect()
        {
            if (Fail) throw new IOException("Synthetic probe error");
            return new EnvironmentReport { Available = true, Checks = new List<EnvironmentCheck>() };
        }
    }
    private sealed class FakeBackend : ITiaBackend
    {
        public readonly List<TiaInstance> Instances = new List<TiaInstance> { Instance(21), Instance(22) };
        public readonly List<int> ThreadIds = new List<int>();
        public int Scans, AttachCalls, AttachedId, Released;
        public Exception AttachError, ReadError, DiscoveryError;
        public bool ReleaseError, EmptyProject;
        public ManualResetEventSlim AttachGate;
        public bool IsAvailable { get { return true; } }
        public IReadOnlyList<TiaInstance> Discover()
        {
            ThreadIds.Add(Thread.CurrentThread.ManagedThreadId); Scans++;
            if (DiscoveryError != null) throw DiscoveryError;
            return Instances.ToList().AsReadOnly();
        }
        public IAttachedPortal Attach(TiaInstance instance)
        {
            ThreadIds.Add(Thread.CurrentThread.ManagedThreadId); AttachCalls++; AttachedId = instance.Id;
            if (AttachGate != null && !AttachGate.Wait(5000)) throw new TimeoutException();
            if (AttachError != null) throw AttachError;
            return new Session(this);
        }
        private sealed class Session : IAttachedPortal
        {
            private readonly FakeBackend owner;
            public Session(FakeBackend owner) { this.owner = owner; }
            public IReadOnlyList<ProjectComponent> ReadComponents()
            {
                owner.ThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
                if (owner.ReadError != null) throw owner.ReadError;
                if (owner.EmptyProject) return new List<ProjectComponent>();
                return new[] { new ProjectComponent { Name = "Test project", Kind = "Project", Status = "SIMULATED" }, new ProjectComponent { Name = "PLC", Kind = "PLC / STEP 7", Status = "SIMULATED" } };
            }
            public void Dispose()
            {
                owner.ThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
                if (owner.ReleaseError) throw new IOException("Synthetic release failure");
                owner.Released++;
            }
        }
    }
}
