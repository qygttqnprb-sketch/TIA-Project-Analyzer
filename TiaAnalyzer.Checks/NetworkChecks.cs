using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

internal static class NetworkChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        XNamespace ns = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4";
        var before = Graph(ns, "1", "2", false); var renamed = Graph(ns, "101", "202", false); var negated = Graph(ns, "101", "202", true);
        check(NetworkComparison.Compare(before, renamed, CancellationToken.None).Single().Change == "RepresentationOrComments",
            "consistent LAD UID renumbering is distinguished from changed wiring");
        check(NetworkComparison.Compare(before, negated, CancellationToken.None).Single().Change == "ChangedAtPosition",
            "LAD contact polarity change remains a network-content change");
        var root = Path.Combine(Path.GetTempPath(), "tia-network-check-" + Guid.NewGuid().ToString("N"));
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var instances = await service.DiscoverAsync(); await service.ConnectAsync(instances[0]);
            var firstDir = Path.Combine(root, "first"); var first = await service.ExportPlcAsync(firstDir, null, CancellationToken.None);
            var firstAnalysis = XmlProjectAnalyzer.Analyze(firstDir, first, CancellationToken.None);
            SnapshotStore.Complete(firstDir, first, CancellationToken.None);
            var native = SnapshotComparer.ReadManifest(Path.Combine(firstDir, "manifest.json"));
            native.Artifacts.First(a => a.Fingerprints?.Count > 0).Fingerprints[0].Value = "changed-native-fingerprint";
            var nativePath = Path.Combine(firstDir, "native.json"); JsonFiles.Write(nativePath, native);
            check(SnapshotComparer.Compare(Path.Combine(firstDir, "manifest.json"), nativePath).Changes.Single().Change == "NativeFingerprintChanged",
                "native fingerprint changes are visible even when exported XML bytes are unchanged");
            var secondDir = Path.Combine(root, "second"); var second = await service.ExportPlcAsync(secondDir, null, CancellationToken.None);
            var motor = second.Artifacts.Single(a => a.Name == "ConveyorControl");
            var motorPath = ExportPaths.Resolve(secondDir, motor.RelativePath); File.WriteAllText(motorPath, File.ReadAllText(motorPath).Replace("AND NOT", "OR NOT"));
            motor.Sha256 = SnapshotStore.FileHash(motorPath); // Deliberately establish this changed synthetic comparison fixture.
            motor.Fingerprints = null; // Modified fixture has no freshly queried Siemens fingerprint.
            XmlProjectAnalyzer.Analyze(secondDir, second, CancellationToken.None); SnapshotStore.Complete(secondDir, second, CancellationToken.None);
            var compared = SnapshotComparer.Compare(Path.Combine(firstDir, "manifest.json"), Path.Combine(secondDir, "manifest.json"));
            var detail = compared.Changes.Single(c => c.Name == "ConveyorControl").Networks.Single();
            check(detail.Change == "ChangedAtPosition" && detail.Before.Contains("AND NOT") && detail.After.Contains("OR NOT"),
                "network diff carries the actual before and after interlock expressions");
            File.AppendAllText(motorPath, "tamper"); bool rejected = false;
            try { SnapshotComparer.Compare(Path.Combine(firstDir, "manifest.json"), Path.Combine(secondDir, "manifest.json")); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "comparison rejects modified XML whose bytes disagree with its manifest");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); bool cancelled = false;
                try { SnapshotComparer.Compare(Path.Combine(firstDir, "manifest.json"), Path.Combine(firstDir, "manifest.json"), cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
                check(cancelled, "comparison observes cancellation during integrity verification");
            }
            var focusedDir = Path.Combine(root, "focused"); var focused = await service.ExportPlcAsync(focusedDir, null, CancellationToken.None);
            var focusedAnalysis = XmlProjectAnalyzer.Analyze(focusedDir, focused, CancellationToken.None);
            var focusedZip = PackageWriter.Build(focusedDir, focused, "", new PackageDiagnostics(), CancellationToken.None, focusedAnalysis, "MotorRun");
            var selectionComparison = SnapshotComparer.Compare(Path.Combine(firstDir, "manifest.json"), focusedZip);
            check(selectionComparison.Changes.Count(c => c.Change == "NotIncludedAfter") == 2 && !selectionComparison.Changes.Any(c => c.Change == "Removed"),
                "a focused package does not turn intentionally excluded blocks into PLC deletions");
            var cyclic = new ProjectAnalysis { Blocks = firstAnalysis.Blocks, Assignments = firstAnalysis.Assignments.ToList() };
            var fb = cyclic.Blocks.Single(b => b.Name == "ConveyorControl");
            cyclic.Assignments.Add(new AssignmentEvidence { ArtifactId = fb.ArtifactId, Network = "cycle", Position = 1, Target = "#Cycle", TargetKey = SymbolIdentity.Key("#Cycle"),
                Value = new LogicExpression { Kind = "Reference", Text = "#Cycle", Symbol = SymbolIdentity.Key("#Cycle") }, Guard = SclLogicAnalyzer.True() });
            check(ConditionTracer.Trace(cyclic, "#Cycle", CancellationToken.None).Warnings.Any(w => w.Contains("ciclica")), "condition cycles terminate with an explicit warning");
            var original = XDocument.Parse("<Document><A><Text> </Text></A></Document>", LoadOptions.PreserveWhitespace);
            var modified = XDocument.Parse("<Document><A><Text></Text></A></Document>", LoadOptions.PreserveWhitespace);
            check(XmlEvidence.Hash(original, true) != XmlEvidence.Hash(modified, true), "streamed XML hashing preserves meaningful whitespace in source leaf tokens");
        }
        finally { await service.ShutdownAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static XDocument Graph(XNamespace ns, string access, string part, bool inverted)
    {
        var parts = new XElement(ns + "Parts", new XElement(ns + "Access", new XAttribute("UId", access)),
            new XElement(ns + "Part", new XAttribute("UId", part), new XAttribute("Name", "Contact"), inverted ? new XElement(ns + "Negated", new XAttribute("Name", "operand")) : null));
        var wires = new XElement(ns + "Wires", new XElement(ns + "Wire", new XElement(ns + "IdentCon", new XAttribute("UId", access)),
            new XElement(ns + "NameCon", new XAttribute("UId", part), new XAttribute("Name", "operand"))));
        var unit = new XElement("SW.Blocks.CompileUnit", new XAttribute("ID", "1"), new XElement("NetworkSource", new XElement(ns + "FlgNet", parts, wires)));
        return new XDocument(new XElement("Document", new XElement("SW.Blocks.FC", unit)));
    }
}
