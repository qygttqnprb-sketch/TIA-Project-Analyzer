using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

internal static class ReliabilityChecks
{
    public static async Task Benchmark(string root)
    {
        Directory.CreateDirectory(root);
        var rows = new List<string> { "Synthetic DEMO only; includes diagnostic flush overhead; not a Siemens performance measurement.",
            "Mode\tXML\tEnumeration\tFingerprint\tXmlExport\tCrossReferences\tXmlParsing\tLogicAnalysis\tReportGeneration\tZipCompression (milliseconds)" };
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var list = await service.DiscoverAsync(); await service.ConnectAsync(list[0]);
            foreach (var mode in new[] { ExportMode.Full, ExportMode.Quick, ExportMode.Targeted })
            {
                var directory = Path.Combine(root, mode.ToString());
                using (var run = new ExportRun(directory))
                {
                    var snapshot = await service.ExportPlcAsync(directory, null, CancellationToken.None,
                        new ExportOptions { Run = run, Mode = mode, TargetObjects = new List<string> { "ConveyorControl" } });
                    PackageWriter.Build(directory, snapshot, "Synthetic benchmark", null, CancellationToken.None, null, null, run);
                    snapshot.Metrics.PhaseMilliseconds = run.Timings();
                    CheckpointStore.Write(Path.Combine(directory, "metrics.json"), snapshot.Metrics);
                    run.Finish(snapshot.AcquisitionState);
                    rows.Add(mode + "\t" + snapshot.Artifacts.Count + "\t" + string.Join("\t", new[] { "Enumeration", "Fingerprint", "XmlExport", "CrossReferences", "XmlParsing", "LogicAnalysis", "ReportGeneration", "ZipCompression" }
                        .Select(p => snapshot.Metrics.SkippedPhases.Contains(p) ? "SKIPPED" : run.Timings().ContainsKey(p) ? run.Timings()[p].ToString() : "NOT RUN")));
                }
            }
        }
        finally { await service.ShutdownAsync(); }
        File.WriteAllLines(Path.Combine(root, "summary.tsv"), rows); foreach (var row in rows) Console.WriteLine(row);
    }
    private static ExportStamp Stamp(string value = "fixture-content", bool reliable = true)
    { return new ExportStamp { Token = value, ReliableForXml = reliable, Fingerprints = new List<ObjectFingerprint> { new ObjectFingerprint { Kind = "TestFullXml", Value = value } } }; }
    private static PlcExportWriter Writer(string dir, ExportOptions options, string identity = "fixture-project", string proof = "fixture-project-proof")
    { return new PlcExportWriter(dir, false, null, CancellationToken.None, options) { ApiIdentity = "fixture-v21", SourceIdentity = identity, SourceProof = proof }; }
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-reliability-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var firstDir = Path.Combine(root, "first");
            using (var run = new ExportRun(firstDir))
            {
                var writer = Writer(firstDir, new ExportOptions { Run = run });
                var called = false;
                writer.Write("P", "PLC", "Blocks", "FC5_Robot_Head_Shell", "FC", "SCL", false, file =>
                {
                    called = true;
                    check(File.ReadAllText(Path.Combine(firstDir, "operations.jsonl")).Contains("Export.XML"), "persistent Start exists before XML callback is entered");
                    check(Path.GetDirectoryName(file.FullName).EndsWith(".partial") && !Directory.GetFiles(firstDir, "*.xml", SearchOption.AllDirectories).Any(), "XML written to staging path, final XML not yet visible");
                    File.WriteAllText(file.FullName, "<Document><Block/></Document>");
                }, readStamp: () => Stamp());
                check(called && CheckpointStore.Read(firstDir).Entries.Count == 1 && run.Status().LastCompleted == "FC5_Robot_Head_Shell", "atomic XML and checkpoint published before last-completed advances");
                var gate = new ManualResetEventSlim(false); var entered = new ManualResetEventSlim(false);
                using (var cancel = new CancellationTokenSource())
                {
                    var pending = Task.Run(() => run.Call("GetCrossReferences(AllObjects)", "CrossReferences", "FC5_Robot_Head_Shell", "FC", "P/PLC/Blocks/FC5", () =>
                    { entered.Set(); if (!gate.Wait(10000)) throw new TimeoutException(); }));
                    check(entered.Wait(5000), "simulated cross-reference entered after FC5 XML completion");
                    var before = run.Status().ElapsedMilliseconds;
                    await Task.Delay(1100);
                    run.PollWatchdog(TimeSpan.FromSeconds(301)); run.PollWatchdog(TimeSpan.FromSeconds(301));
                    var text = File.ReadAllText(Path.Combine(firstDir, "operations.jsonl"));
                    check(text.Split(new[] { "Watchdog" }, StringSplitOptions.None).Length - 1 == 3 && text.Contains("30 s") && text.Contains("120 s") && text.Contains("300 s"), "watchdog emits each 30/120/300 threshold once per operation");
                    check(!pending.IsCompleted && run.Status().ElapsedMilliseconds > before && run.Status().Operation.Contains("CrossReferences"), "independent heartbeat advances while simulated API is blocked");
                    cancel.Cancel();
                    check(!pending.IsCompleted && run.Status().LastCompleted == "FC5_Robot_Head_Shell", "cancel request does not abort API or blame last XML");
                    gate.Set(); await pending;
                }
                check(run.Timings()["CrossReferences"] >= 1000 && run.Status().Events.Count == 5, "cross-reference phase measured separately and recent events bounded to five");
            }
            int exports = 0;
            var secondDir = Path.Combine(root, "resumed");
            var resumed = Writer(secondDir, new ExportOptions { ResumeDirectory = firstDir });
            var hit = resumed.Write("P", "PLC", "Blocks", "FC5_Robot_Head_Shell", "FC", "SCL", false,
                file => { exports++; File.WriteAllText(file.FullName, "<fresh/>"); }, readStamp: () => Stamp());
            check(hit.ReusedXml && exports == 0 && hit.Sha256 == CheckpointStore.Read(firstDir).Entries[0].Artifact.Sha256, "resume verifies project/API/object proofs and SHA256 before reusing XML");
            foreach (var item in new[] { "identity", "proof", "api", "missing-proof", "unreliable", "changed", "language", "protected" })
            {
                var next = Writer(Path.Combine(root, item), new ExportOptions { ResumeDirectory = firstDir }, item == "identity" ? "other-project" : "fixture-project", item == "proof" ? "other-proof" : item == "missing-proof" ? null : "fixture-project-proof");
                if (item == "api") next.ApiIdentity = "different-api";
                var artifact = next.Write("P", "PLC", "Blocks", "FC5_Robot_Head_Shell", "FC", item == "language" ? "LAD" : "SCL", item == "protected",
                    file => { exports++; File.WriteAllText(file.FullName, "<fresh/>"); }, readStamp: () => Stamp(item == "changed" ? "changed" : "fixture-content", item != "unreliable"));
                check(!artifact.ReusedXml, "resume re-exports on " + item);
            }
            var original = CheckpointStore.Read(firstDir).Entries.Single();
            File.WriteAllText(ExportPaths.Resolve(firstDir, original.Artifact.RelativePath), "<tampered/>");
            var tampered = Writer(Path.Combine(root, "tampered"), new ExportOptions { ResumeDirectory = firstDir });
            var repaired = tampered.Write("P", "PLC", "Blocks", "FC5_Robot_Head_Shell", "FC", "SCL", false, file => File.WriteAllText(file.FullName, "<fresh/>"), readStamp: () => Stamp());
            check(!repaired.ReusedXml && repaired.Status == "Exported", "tampered checkpoint XML re-exported rather than trusted");
            File.Delete(ExportPaths.Resolve(firstDir, original.Artifact.RelativePath));
            var missing = Writer(Path.Combine(root, "missing"), new ExportOptions { ResumeDirectory = firstDir });
            check(!missing.Write("P", "PLC", "Blocks", "FC5_Robot_Head_Shell", "FC", "SCL", false, f => File.WriteAllText(f.FullName, "<fresh/>"), readStamp: () => Stamp()).ReusedXml, "missing checkpoint file re-exported");
            original.Artifact.RelativePath += ".partial";
            check(!CheckpointStore.Restore(firstDir, original, Path.Combine(root, "should-not-exist"), "fixture-project", "fixture-project-proof", "fixture-v21", "fixture-content", CancellationToken.None), "incomplete staging files never resumable");
            var invalid = Writer(Path.Combine(root, "invalid-xml"), new ExportOptions());
            invalid.Write("P", "PLC", "Blocks", "Bad", "FC", "SCL", false, f => File.WriteAllText(f.FullName, "<unclosed>"));
            check(invalid.Snapshot.Artifacts[0].Status == "Failed" && CheckpointStore.Read(Path.Combine(root, "invalid-xml")).Entries.Count == 0, "malformed XML has no completed checkpoint");
            var bounded = Writer(Path.Combine(root, "bounded-references"), new ExportOptions()); bounded.CrossReferenceVisits = 199998;
            int enumerations = 0;
            Func<IEnumerable<int>> source = () => { enumerations++; return Enumerable.Range(0, 10); };
            check(bounded.Enumerate("Fixture.References", "fixture", source, "CrossReferences").Count() == 2 &&
                !bounded.Enumerate("Fixture.References", "fixture", source, "CrossReferences").Any() && enumerations == 1,
                "cross-reference budget bounds references/locations and avoids further enumeration after exhaustion");
            await ModeChecks(root, check);
            await BlockedViewModel(root, check);
            var cancelled = new ExportSnapshot { AcquisitionState = "Cancelled" };
            bool refused = false;
            try { PackageWriter.Build(Path.Combine(root, "cancelled"), cancelled, "", null, CancellationToken.None); } catch (InvalidDataException) { refused = true; }
            check(refused && !File.Exists(Path.Combine(root, "cancelled.zip")), "cancelled snapshot cannot be published as complete ZIP");
            var corrupt = Path.Combine(root, "corrupt-checkpoint"); Directory.CreateDirectory(corrupt);
            File.WriteAllText(Path.Combine(corrupt, "checkpoint.json"), "not json");
            var fallback = Writer(Path.Combine(root, "fallback"), new ExportOptions { ResumeDirectory = corrupt });
            check(fallback.Snapshot.Notes.Any(n => n.Contains("ignorato")), "malformed checkpoint diagnosed and ignored");
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task BlockedViewModel(string root, Action<bool, string> check)
    {
        var backend = new BlockedBackend();
        var real = new TiaConnectionService(backend); var demo = new TiaConnectionService(new DemoBackend());
        var vm = new TiaAnalyzer.App.ViewModels.MainViewModel(new MockEnvironmentProbe(), new MockEnvironmentProbe(), real, demo);
        try
        {
            await vm.InitializeAsync(); await vm.ScanCommand.ExecuteAsync(); vm.SelectedInstance = vm.Instances[0]; vm.RealConsent = true;
            await vm.ConnectCommand.ExecuteAsync(); vm.ExportRoot = Path.Combine(root, "vm");
            var pending = vm.ExportXmlCommand.ExecuteAsync();
            check(backend.Entered.Wait(5000), "VM export reaches a simulated blocking fingerprint after one completed XML");
            await Task.Delay(1300);
            check(!pending.IsCompleted && vm.LiveExportStatus.Contains("Simulated.BlockingFingerprint") && vm.LiveExportStatus.Contains("FC5"), "VM heartbeat shows current operation and last completed XML while worker is blocked");
            await vm.CancelExportCommand.ExecuteAsync();
            check(!pending.IsCompleted, "VM cancellation remains pending while simulated API owns the worker");
            backend.Release.Set(); await pending;
            var directory = Directory.GetDirectories(vm.ExportRoot).Single();
            var checkpoint = CheckpointStore.Read(directory);
            check(checkpoint.State == "Cancelled" && checkpoint.Entries.Count == 1 && !File.Exists(Path.Combine(directory, "manifest.json")), "cancellation preserves completed checkpoint and publishes no complete manifest");
            check(vm.LastCheckpoint.Contains("checkpoint.json") && vm.ExportStatus.Contains("annullata"), "terminal VM status retains last valid checkpoint");
        }
        finally { backend.Release.Set(); await real.ShutdownAsync(); await demo.ShutdownAsync(); }
        var worker = new SerialWorker();
        bool rejected = false;
        try { await worker.Run(() => worker.Run(() => true)); } catch (InvalidOperationException) { rejected = true; }
        finally { await worker.StopAsync(); }
        check(rejected, "worker rejects self-queued operations instead of allowing a synchronization deadlock");
    }
    private sealed class BlockedBackend : ITiaBackend
    {
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
        public bool IsAvailable { get { return true; } }
        public IReadOnlyList<TiaInstance> Discover() { return new[] { new TiaInstance { Id = 1, Version = "SIMULATED", ExecutablePath = "fixture" } }; }
        public IAttachedPortal Attach(TiaInstance instance) { return new Session(this); }
        private sealed class Session : IAttachedPortal, IPlcExportSession
        {
            private readonly BlockedBackend owner;
            public Session(BlockedBackend owner) { this.owner = owner; }
            public IReadOnlyList<ProjectComponent> ReadComponents() { return new List<ProjectComponent>(); }
            public void Dispose() { }
            public ExportSnapshot ExportPlc(string directory, IProgress<ExportProgress> progress, CancellationToken cancellation, ExportOptions options = null)
            {
                var writer = new PlcExportWriter(directory, true, progress, cancellation, options) { SourceIdentity = "fixture", ApiIdentity = "fixture" };
                writer.Write("P", "PLC", "Blocks", "FC5", "FC", "SCL", false, f => File.WriteAllText(f.FullName, "<Document/>"), readStamp: () => Stamp());
                options.Run.Call("Simulated.BlockingFingerprint", "Fingerprint", "NextObject", "FC", "P/PLC/Blocks/NextObject", () =>
                { owner.Entered.Set(); if (!owner.Release.Wait(10000)) throw new TimeoutException(); });
                writer.CheckCancellation(); return writer.Snapshot;
            }
        }
    }
    private static async Task ModeChecks(string root, Action<bool, string> check)
    {
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var list = await service.DiscoverAsync(); await service.ConnectAsync(list[0]);
            var quickDir = Path.Combine(root, "quick");
            var quick = await service.ExportPlcAsync(quickDir, null, CancellationToken.None, new ExportOptions { Mode = ExportMode.Quick });
            check(quick.AcquisitionState == "Partial" && quick.Artifacts.Count == 6 && quick.CrossReferences.Count == 0 && quick.Artifacts.All(a => a.Fingerprints == null), "Quick exports XML but skips optional fingerprints/cross-references and declares partial coverage");
            var targetedDir = Path.Combine(root, "targeted");
            var targeted = await service.ExportPlcAsync(targetedDir, null, CancellationToken.None,
                new ExportOptions { Mode = ExportMode.Targeted, TargetObjects = new List<string> { "ConveyorControl" } });
            check(targeted.Artifacts.Count == 1 && targeted.Inventory.Count == 6 && targeted.Selection.ExcludedIds.Count == 5 && targeted.Selection.IncludedArtifacts == 1, "Targeted exports only named object and declares all included/excluded IDs");
            var archive = PackageWriter.Build(targetedDir, targeted, "Fixture only", null, CancellationToken.None);
            using (var zip = ZipFile.OpenRead(archive)) check(zip.Entries.Count(e => e.FullName.EndsWith(".xml")) == 1 && zip.GetEntry("export-inventory.json") != null, "targeted ZIP contains exactly selected XML plus explicit inventory");
            check(SnapshotReader.Open(archive, CancellationToken.None).Warnings.Any(w => w.Contains("Stato raccolta")), "offline reader retains partial-collection warning");
            var fullDir = Path.Combine(root, "full");
            var full = await service.ExportPlcAsync(fullDir, null, CancellationToken.None);
            SnapshotStore.Complete(fullDir, full, CancellationToken.None);
            check(full.AcquisitionState == "Complete" && CheckpointStore.Read(fullDir).State == "Complete", "Complete state requires finished collection and verified manifest");
            full.AcquisitionState = null; full.ExportMode = null; full.ExportId = null; full.Inventory = null;
            var legacyPath = Path.Combine(fullDir, "legacy-v09.json"); JsonFiles.Write(legacyPath, full);
            check(SnapshotReader.Open(legacyPath, CancellationToken.None).VerifiedFiles == 6, "V0.9 manifests without V1.0 optional metadata remain readable");
            var badArchive = Path.Combine(root, "bad-hash.zip"); bool badHashRefused = false;
            try { PackageWriter.CreateArchive(fullDir, badArchive, new[] { full.Artifacts[0].RelativePath }, CancellationToken.None,
                expectedHashes: new Dictionary<string, string> { { full.Artifacts[0].RelativePath, new string('0', 64) } }); }
            catch (InvalidDataException) { badHashRefused = true; }
            check(badHashRefused && !File.Exists(badArchive), "ZIP hashes the actual copied XML bytes and refuses mismatched payloads");
            var changedDir = Path.Combine(root, "changed-after-checkpoint");
            var changed = await service.ExportPlcAsync(changedDir, null, CancellationToken.None);
            File.AppendAllText(ExportPaths.Resolve(changedDir, changed.Artifacts[0].RelativePath), " ");
            bool changedRefused = false;
            try { SnapshotStore.Complete(changedDir, changed, CancellationToken.None); } catch (InvalidDataException) { changedRefused = true; }
            check(changedRefused && !File.Exists(Path.Combine(changedDir, "manifest.json")), "manifest completion rejects XML changed after checkpoint");
            var badTarget = Path.Combine(root, "unknown-target"); bool unknown = false;
            try { await service.ExportPlcAsync(badTarget, null, CancellationToken.None, new ExportOptions { Mode = ExportMode.Targeted, TargetObjects = new List<string> { "DoesNotExist" } }); }
            catch (InvalidOperationException) { unknown = true; }
            check(unknown && !File.Exists(Path.Combine(badTarget, "manifest.json")), "unknown targeted objects fail before export and never produce complete manifest");
        }
        finally { await service.ShutdownAsync(); }
    }
}
