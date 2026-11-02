using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TiaAnalyzer.App.ViewModels;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        passed++;
    }

    private static async Task Main()
    {
        // A real adapter that fails immediately if any VM path calls it.
        var realConnection = new ForbiddenConnection();
        var real = new Probe(false);
        var demo = new Probe(true);
        var vm = new MainViewModel(real, demo, realConnection, new DemoConnectionService());
        await vm.InitializeAsync();
        Check(real.Calls == 1 && !vm.ConnectCommand.CanExecute(null), "automatic real check; real connection disabled");
        vm.ConnectCommand.Execute(null);
        Check(realConnection.Calls == 0, "disabled real command cannot invoke adapter");
        await SwitchMode(vm, true);
        Check(demo.Calls == 1 && vm.ConnectCommand.CanExecute(null), "demo readiness enables connection");
        vm.ConnectCommand.Execute(null);
        Check(vm.Components.Count == 6 && vm.Components.All(c => c.Status == "DEMO"), "six explicitly synthetic components");
        Check(vm.SessionStatus.StartsWith("MOCK connesso") && !vm.ConnectCommand.CanExecute(null), "demo connected; duplicate connection disabled");
        vm.DisconnectCommand.Execute(null);
        Check(vm.Components.Count == 0 && vm.SessionStatus == "Disconnesso", "disconnect clears synthetic state");
        vm.ConnectCommand.Execute(null);
        await SwitchMode(vm, false);
        Check(vm.Components.Count == 0 && vm.SessionStatus == "Disconnesso" && !vm.ConnectCommand.CanExecute(null), "switch to real clears session and data");
        Check(realConnection.Calls == 0, "demo workflow never invokes Siemens adapter");

        demo.Fail = true;
        await SwitchMode(vm, true);
        Check(vm.EnvironmentStatus == "Verifica non riuscita" && vm.Checks[0].Status == "ERRORE" && !vm.ConnectCommand.CanExecute(null), "probe failure displayed and connection blocked");
        demo.Fail = false;
        await vm.CheckCommand.ExecuteAsync();
        Check(vm.ConnectCommand.CanExecute(null) && !vm.IsBusy, "probe retry recovers commands");

        var gate = new BlockingProbe();
        var waiting = new MainViewModel(gate, demo, realConnection, new DemoConnectionService());
        var pending = waiting.InitializeAsync();
        Check(waiting.IsBusy && !waiting.CanChangeMode && !waiting.CheckCommand.CanExecute(null), "busy state blocks overlapping checks");
        waiting.IsDemo = true;
        await waiting.InitializeAsync();
        gate.Release.Set();
        await pending;
        Check(!waiting.IsDemo && gate.Calls == 1 && !waiting.IsBusy, "mode changes ignored while probing; no stale mode result");

        var planner = new FutureExportPlanner();
        Check(!planner.CanExport && planner.Describe(ExportFormat.Zip).PlannedEntries.Count == 3 && planner.Describe(ExportFormat.Xml).Format == ExportFormat.Xml, "XML and ZIP plans only; export unavailable");
        var unavailable = new SiemensConnectionService();
        bool refused = false;
        try { unavailable.Connect(); } catch (NotSupportedException) { refused = true; }
        Check(!unavailable.CanConnect && refused, "real adapter fails closed");
        var report = new MockEnvironmentProbe().Inspect();
        Check(report.Available && report.Checks.Count == 3 && report.Checks.All(c => c.Status == "MOCK"), "demo probe reports only simulated readiness");
        var temp = Path.Combine(Path.GetTempPath(), "tia-checks-" + Guid.NewGuid());
        try
        {
            var missing = new SiemensEnvironmentProbe(temp).Inspect();
            Check(!missing.Available && missing.Checks.Count == 6 && missing.Checks.Count(c => c.Status == "ERRORE") >= 3, "missing Siemens folder and DLLs diagnosed");
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "Siemens.Engineering.Base.dll"), "invalid assembly");
            File.Copy(typeof(Program).Assembly.Location, Path.Combine(temp, "Siemens.Engineering.Step7.dll"));
            var invalid = new SiemensEnvironmentProbe(temp).Inspect();
            Check(!invalid.Available && invalid.Checks[2].Status == "ERRORE" && invalid.Checks[3].Status == "ERRORE", "invalid binary and wrong assembly identity rejected");
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        Console.WriteLine(passed + " checks passed (portable source-level checks; Siemens/WPF not validated).");
    }

    private static async Task SwitchMode(MainViewModel vm, bool demo)
    {
        var completion = new TaskCompletionSource<bool>();
        System.ComponentModel.PropertyChangedEventHandler handler = (sender, args) =>
        {
            if (args.PropertyName == "IsBusy" && !vm.IsBusy) completion.TrySetResult(true);
        };
        vm.PropertyChanged += handler;
        try
        {
            vm.IsDemo = demo;
            if (await Task.WhenAny(completion.Task, Task.Delay(5000)) != completion.Task)
                throw new Exception("Mode check timed out");
            // Mode command handles exceptions and resets command state after IsBusy.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!vm.CheckCommand.CanExecute(null))
            {
                if (DateTime.UtcNow > deadline) throw new Exception("Command recovery timed out");
                await Task.Delay(1);
            }
        }
        finally { vm.PropertyChanged -= handler; }
    }

    private sealed class Probe : IEnvironmentProbe
    {
        public int Calls;
        public bool Fail;
        private readonly bool available;
        public Probe(bool available) { this.available = available; }
        public EnvironmentReport Inspect()
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("Synthetic probe error");
            return new EnvironmentReport { Available = available, Checks = new List<EnvironmentCheck>() };
        }
    }
    private sealed class BlockingProbe : IEnvironmentProbe
    {
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
        public int Calls;
        public EnvironmentReport Inspect()
        {
            Interlocked.Increment(ref Calls);
            if (!Release.Wait(5000)) throw new TimeoutException();
            return new EnvironmentReport { Available = false, Checks = new List<EnvironmentCheck>() };
        }
    }
    private sealed class ForbiddenConnection : ITiaConnectionService
    {
        public int Calls;
        public bool CanConnect { get { return false; } }
        public IReadOnlyTiaSession Connect() { Calls++; throw new Exception("Unexpected real connection"); }
        public IReadOnlyList<ProjectComponent> GetComponents(IReadOnlyTiaSession session) { Calls++; throw new Exception("Unexpected project read"); }
    }
}
