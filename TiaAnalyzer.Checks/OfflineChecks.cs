using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;
using TiaAnalyzer.App.ViewModels;

internal static class OfflineChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-offline-" + Guid.NewGuid().ToString("N"));
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var instances = await service.DiscoverAsync(); await service.ConnectAsync(instances[0]);
            var directory = Path.Combine(root, "source");
            var snapshot = await service.ExportPlcAsync(directory, null, CancellationToken.None);
            var zip = PackageWriter.Build(directory, snapshot, "Motore fermo", new PackageDiagnostics(), CancellationToken.None);
            await service.DisconnectAsync();
            var bytes = File.ReadAllBytes(zip);
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
            var loaded = SnapshotReader.Open(zip, CancellationToken.None);
            check(!service.HasSession && loaded.VerifiedFiles == 6 && loaded.Analysis.Blocks.Count == 6 && loaded.Snapshot.IsDemo,
                "offline ZIP opens and verifies all XML without an attached TIA session");
            check(loaded.Analysis.Assignments.Count(a => a.TargetKey == SymbolIdentity.Key("MotorRun")) == 2 &&
                loaded.Analysis.Writes.Groups.Any(g => g.Classification == "MultipleSites"), "offline open recomputes conditions and writer sites");
            check(XmlProjectAnalyzer.Search(loaded.Analysis, "SafetyOK").Count == 3 &&
                ConditionTracer.Trace(loaded.Analysis, "MotorRun", CancellationToken.None).Steps.Any(s => s.Description.Contains("JamDetected")),
                "offline snapshot supports symbol lookup and complete modeled condition traces");
            var motor = loaded.Analysis.Blocks.Single(b => b.Name == "ConveyorControl");
            var preview = SnapshotReader.Preview(loaded, motor.ArtifactId, motor.Networks[0].Id, CancellationToken.None);
            check(preview.Text.Contains("JamDetected") && preview.File == motor.File && !preview.Truncated,
                "offline network preview verifies and reads its original NetworkSource");
            check(bytes.SequenceEqual(File.ReadAllBytes(zip)) && files.SequenceEqual(Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(p => p)),
                "offline open and preview neither extract ZIP entries nor write source reports");
            var folder = SnapshotReader.Open(Path.Combine(directory, "manifest.json"), CancellationToken.None);
            check(folder.VerifiedFiles == 6 && folder.Analysis.Assignments.Count == loaded.Analysis.Assignments.Count,
                "manifest-folder reader and ZIP reader produce the same modeled assignments");
            var forged = Copy(zip, root, "forged");
            using (var archive = TestFiles.OpenArchiveForUpdate(forged))
                foreach (var name in new[] { "analysis.json", "writers.json" })
                {
                    archive.GetEntry(name).Delete();
                    using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write("not JSON: do not trust cached reports");
                }
            check(SnapshotReader.Open(forged, CancellationToken.None).Analysis.Writes.Groups.Any(g => g.TargetKey == SymbolIdentity.Key("MotorRun")),
                "serialized analysis and writer reports are ignored in favor of verified XML");
            var tampered = Copy(zip, root, "tampered");
            Edit(tampered, snapshot.Artifacts[0].RelativePath, "<Document>tampered</Document>");
            check(Rejected(() => SnapshotReader.Open(tampered, CancellationToken.None)), "offline open rejects XML whose bytes disagree with the manifest");
            var missing = Copy(zip, root, "missing");
            Edit(missing, snapshot.Artifacts[0].RelativePath, null);
            check(Rejected(() => SnapshotReader.Open(missing, CancellationToken.None)), "declared exported XML missing from a ZIP aborts the open");
            var duplicate = Copy(zip, root, "duplicate");
            using (var archive = TestFiles.OpenArchiveForUpdate(duplicate)) archive.CreateEntry(snapshot.Artifacts[0].RelativePath);
            check(Rejected(() => SnapshotReader.Open(duplicate, CancellationToken.None)), "duplicate expected XML entries are rejected");
            var duplicateManifest = Copy(zip, root, "duplicate-manifest");
            using (var archive = TestFiles.OpenArchiveForUpdate(duplicateManifest)) archive.CreateEntry("manifest.json");
            check(Rejected(() => SnapshotReader.Open(duplicateManifest, CancellationToken.None)), "duplicate manifests are rejected before analysis");
            var unsafePath = Copy(zip, root, "unsafe");
            var manifest = SnapshotComparer.ReadManifest(zip); manifest.Artifacts[0].RelativePath = "../outside.xml";
            SetManifest(unsafePath, manifest);
            check(Rejected(() => SnapshotReader.Open(unsafePath, CancellationToken.None)), "manifest path traversal cannot reach local files");
            var sharedPath = Copy(zip, root, "shared-path");
            manifest = SnapshotComparer.ReadManifest(zip); manifest.Artifacts[1].RelativePath = manifest.Artifacts[0].RelativePath.ToUpperInvariant();
            SetManifest(sharedPath, manifest);
            check(Rejected(() => SnapshotReader.Open(sharedPath, CancellationToken.None)), "Windows case aliases cannot give two artifacts the same XML path");
            var extra = Copy(zip, root, "extra");
            using (var archive = TestFiles.OpenArchiveForUpdate(extra))
            using (var writer = new StreamWriter(archive.CreateEntry("../../outside.xml").Open())) writer.Write("must not be extracted");
            check(SnapshotReader.Open(extra, CancellationToken.None).VerifiedFiles == 6 && !File.Exists(Path.Combine(root, "outside.xml")),
                "unreferenced unsafe entries are ignored and never extracted");
            var badSelection = Copy(zip, root, "selection");
            manifest = SnapshotComparer.ReadManifest(zip); manifest.Selection = new PackageSelection { IncludedArtifacts = 6, TotalArtifacts = 99 };
            SetManifest(badSelection, manifest);
            check(Rejected(() => SnapshotReader.Open(badSelection, CancellationToken.None)), "inconsistent selection counts cannot claim complete coverage");
            var old = Copy(zip, root, "old");
            manifest = SnapshotComparer.ReadManifest(zip); manifest.Notes = null; manifest.Components = null; manifest.CrossReferences = null; manifest.Limitations = null;
            SetManifest(old, manifest);
            check(SnapshotReader.Open(old, CancellationToken.None).Analysis.Blocks.Count == 6,
                "older manifests with missing optional collections remain readable");
            var nullReference = Copy(zip, root, "null-reference");
            manifest = SnapshotComparer.ReadManifest(zip); manifest.CrossReferences.Add(null); SetManifest(nullReference, manifest);
            check(Rejected(() => SnapshotReader.Open(nullReference, CancellationToken.None)), "malformed cross-reference rows are rejected as invalid data");
            manifest = SnapshotComparer.ReadManifest(zip); var failed = manifest.Artifacts.Single(a => a.Name == "MotorState");
            failed.Status = "Failed"; failed.RelativePath = null;
            var failedManifest = Path.Combine(directory, "failed-manifest.json"); JsonFiles.Write(failedManifest, manifest);
            var partial = SnapshotReader.Open(failedManifest, CancellationToken.None);
            check(partial.VerifiedFiles == 5 && partial.Snapshot.Artifacts.Count == 6 && partial.Warnings.Any(w => w.Contains("Failed")),
                "objects explicitly declared Failed remain visible as missing coverage");
            manifest = SnapshotComparer.ReadManifest(zip);
            var badXml = Path.Combine(directory, "bad.xml");
            File.WriteAllText(badXml, "<!DOCTYPE Document [<!ENTITY x SYSTEM 'file:///must-not-read'>]><Document>&x;</Document>");
            manifest.Artifacts[0].RelativePath = "bad.xml"; manifest.Artifacts[0].Sha256 = SnapshotStore.FileHash(badXml); manifest.Artifacts[0].Bytes = new FileInfo(badXml).Length;
            var dtd = Path.Combine(directory, "dtd-manifest.json"); JsonFiles.Write(dtd, manifest);
            check(Rejected(() => SnapshotReader.Open(dtd, CancellationToken.None)), "matching hashes do not authorize XML DTDs or external entities");
            check(Rejected(() => SnapshotReader.Preview(loaded, motor.ArtifactId, "missing-network", CancellationToken.None)),
                "preview never substitutes a different network for an unknown ID");
            check(Rejected(() => SnapshotReader.Preview(loaded, ExportPaths.Hash("not included"), "1", CancellationToken.None)),
                "preview is restricted to artifacts included in the loaded context");
            var longDirectory = Path.Combine(root, "long-preview"); Directory.CreateDirectory(longDirectory);
            var longPath = Path.Combine(longDirectory, "long.xml");
            new XDocument(new XElement("Document", new XElement("SW.Blocks.FC", new XElement("ObjectList",
                new XElement("SW.Blocks.CompileUnit", new XAttribute("ID", "1"), new XElement("AttributeList",
                    new XElement("NetworkSource", new XElement("StructuredText", new XElement("LineComment", new XElement("Text", new string('x', 20000))))))))))).Save(longPath);
            var longId = ExportPaths.Hash("long-preview");
            var longSnapshot = new ExportSnapshot { IsDemo = true, Artifacts = new List<ExportArtifact> {
                new ExportArtifact { Id = longId, Project = "P", Plc = "PLC", Name = "Long", Kind = "FC", Language = "SCL", Status = "Exported",
                    RelativePath = "long.xml", Sha256 = SnapshotStore.FileHash(longPath), Bytes = new FileInfo(longPath).Length } } };
            var longManifest = Path.Combine(longDirectory, "manifest.json"); JsonFiles.Write(longManifest, longSnapshot);
            var bounded = SnapshotReader.Preview(SnapshotReader.Open(longManifest, CancellationToken.None), longId, "1", CancellationToken.None);
            check(bounded.Truncated && bounded.Text.Length == 16000, "large network previews declare truncation and retain the complete original XML");
            bool cancelled = false;
            try { SnapshotReader.Open(zip, new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "offline open observes cancellation before touching its source");
            var mutable = Copy(zip, root, "mutable");
            var prior = SnapshotReader.Open(mutable, CancellationToken.None);
            Edit(mutable, motor.File, "<Document>changed after load</Document>");
            check(Rejected(() => SnapshotReader.Preview(prior, motor.ArtifactId, motor.Networks[0].Id, CancellationToken.None)),
                "network preview rejects source changes after a successful load");
            var focusDirectory = Path.Combine(root, "focus");
            await service.ConnectAsync((await service.DiscoverAsync())[0]);
            var focused = await service.ExportPlcAsync(focusDirectory, null, CancellationToken.None);
            var focusedZip = PackageWriter.Build(focusDirectory, focused, "", new PackageDiagnostics(), CancellationToken.None, null, "MotorRun");
            await service.DisconnectAsync();
            var selection = SnapshotReader.Open(focusedZip, CancellationToken.None);
            check(selection.VerifiedFiles == 4 && selection.Snapshot.Selection.TotalArtifacts == 6 && selection.Warnings.Any(w => w.Contains("mirato")),
                "offline focused packages retain their declared exclusions and coverage");
            await ViewModelChecks(zip, root, check);
        }
        finally
        {
            await service.ShutdownAsync();
            TestFiles.DeleteOwnedTemporary(root, "tia-offline-");
        }
    }
    private static async Task ViewModelChecks(string zip, string root, Action<bool, string> check)
    {
        var real = new TiaConnectionService(new DemoBackend());
        var demo = new TiaConnectionService(new DemoBackend());
        var vm = new MainViewModel(new MissingProbe(), new MissingProbe(), real, demo);
        try
        {
            await vm.InitializeAsync();
            vm.OpenSnapshotPath = zip; vm.ExportRoot = Path.Combine(root, "ui-output");
            check(!vm.ScanCommand.CanExecute(null) && vm.OpenSnapshotCommand.CanExecute(null), "missing Siemens diagnostics cannot disable offline open");
            await vm.OpenSnapshotCommand.ExecuteAsync();
            check(vm.AnalysisSource.Contains("ARCHIVIO DEMO") && !real.HasSession && !demo.HasSession &&
                vm.SnapshotOpenStatus.Contains("6 XML"), "offline view model opens demo provenance independently of its real connection mode");
            vm.SearchTerm = "MotorRun"; vm.SearchSymbolsCommand.Execute(null);
            await vm.WritersCommand.ExecuteAsync(); await vm.TraceCommand.ExecuteAsync();
            check(vm.WriterReport.Contains("MotorRun") && vm.TraceReport.Contains("JamDetected"), "offline UI commands reuse writer and condition tools without a session");
            vm.SelectedNetwork = vm.Networks.First(n => n.Display.Contains("ConveyorControl"));
            await vm.PreviewNetworkCommand.ExecuteAsync();
            check(vm.SourcePreview.Contains("JamDetected"), "offline network selection reads the requested original source");
            var source = vm.AnalysisSource;
            vm.OpenSnapshotPath = Path.Combine(root, "missing.zip"); await vm.OpenSnapshotCommand.ExecuteAsync();
            check(vm.AnalysisSource == source && vm.SnapshotOpenStatus.Contains("non riuscita") && vm.SearchSymbolsCommand.CanExecute(null),
                "failed offline load preserves the previous verified analysis context");
        }
        finally { await vm.CloseAsync(); }
    }
    private sealed class MissingProbe : IEnvironmentProbe
    {
        public EnvironmentReport Inspect() { throw new IOException("Siemens API unavailable for this synthetic check."); }
    }
    private static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is XmlException || ex is ArgumentException) { return true; }
    }
    private static string Copy(string source, string root, string name) { var path = Path.Combine(root, name + ".zip"); File.Copy(source, path); return path; }
    private static void Edit(string path, string entryName, string text)
    {
        using (var archive = TestFiles.OpenArchiveForUpdate(path))
        {
            archive.GetEntry(entryName)?.Delete();
            if (text != null) using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open())) writer.Write(text);
        }
    }
    private static void SetManifest(string path, ExportSnapshot snapshot)
    {
        using (var archive = TestFiles.OpenArchiveForUpdate(path))
        {
            archive.GetEntry("manifest.json").Delete();
            using (var stream = archive.CreateEntry("manifest.json").Open()) new DataContractJsonSerializer(typeof(ExportSnapshot)).WriteObject(stream, snapshot);
        }
    }
}
