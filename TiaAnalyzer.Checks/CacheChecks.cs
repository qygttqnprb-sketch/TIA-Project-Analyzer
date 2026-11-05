using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

internal static class CacheChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-cache-check-" + Guid.NewGuid().ToString("N"));
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var instances = await service.DiscoverAsync(); await service.ConnectAsync(instances[0]);
            var options = new ExportOptions { CacheDirectory = Path.Combine(root, "cache"), ReuseUnchangedXml = true };
            var analysisOptions = new AnalysisOptions { CacheDirectory = options.CacheDirectory };
            var firstDir = Path.Combine(root, "first"); var first = await service.ExportPlcAsync(firstDir, null, CancellationToken.None, options);
            var firstAnalysis = XmlProjectAnalyzer.Analyze(firstDir, first, CancellationToken.None, analysisOptions);
            check(first.Metrics.XmlExported == 6 && first.Metrics.AnalysisCacheMisses == 6, "cold cache exports and analyzes every demo object");
            var secondDir = Path.Combine(root, "second"); var second = await service.ExportPlcAsync(secondDir, null, CancellationToken.None, options);
            var secondAnalysis = XmlProjectAnalyzer.Analyze(secondDir, second, CancellationToken.None, analysisOptions);
            check(second.Metrics.XmlReused == 5 && second.Metrics.XmlExported == 1, "warm XML cache reuses blocks and UDT but always exports tag tables");
            check(second.Metrics.AnalysisCacheHits == 6 && second.Metrics.AnalysisCacheMisses == 0, "warm analysis cache avoids parsing unchanged XML");
            check(secondAnalysis.Dependencies.Any(d => d.TargetName == "ConveyorControl" && d.Resolution == "Resolved"), "cached fragments resolve dependencies again in the current snapshot");
            check(firstAnalysis.Blocks.All(b => b.Networks.All(n => n.GraphXml == null)) &&
                Directory.GetFiles(secondDir, "*.xml", SearchOption.AllDirectories).Length == 6, "compact reports retain all original XML in every snapshot");
            check(second.CrossReferences.Count == 4 && second.CrossReferences.All(r => r.Provider == "DEMO") &&
                XmlProjectAnalyzer.Search(secondAnalysis, "MotorRun").Any(s => s.Access == "Write"), "fresh demo cross-references preserve access and provenance on a cache hit");
            var cached = Directory.GetFiles(Path.Combine(options.CacheDirectory, "xml-v2"), "*.blob").First();
            File.WriteAllText(cached, "corrupted");
            var recoveryDir = Path.Combine(root, "recovery");
            var recovery = await service.ExportPlcAsync(recoveryDir, null, CancellationToken.None, options);
            check(recovery.Metrics.XmlReused == 4 && recovery.Metrics.XmlExported == 2 && recovery.Metrics.CacheWarnings == 1 && recovery.Issues.Count == 0,
                "corrupt XML cache triggers a full export instead of losing an object");
            var artifact = second.Artifacts.Single(a => a.Name == "ConveyorControl");
            var path = ExportPaths.Resolve(secondDir, artifact.RelativePath);
            File.WriteAllText(path, File.ReadAllText(path).Replace("AND NOT", "OR NOT"));
            var changed = XmlProjectAnalyzer.Analyze(secondDir, second, CancellationToken.None, analysisOptions);
            check(second.Metrics.AnalysisCacheMisses == 1 && changed.Blocks.Single(b => b.Name == "ConveyorControl").Networks[0].Source.Contains("OR NOT"),
                "changed XML invalidates its analysis fragment even if artifact identity is unchanged");
            var savedTarget = second.Artifacts.Single(a => a.Name == "ConveyorControl"); second.Artifacts.Remove(savedTarget);
            var reduced = XmlProjectAnalyzer.Analyze(secondDir, second, CancellationToken.None, analysisOptions);
            check(reduced.Dependencies.Any(d => d.TargetName == "ConveyorControl" && d.TargetId == null), "cached resolution cannot retain a block removed from the current snapshot");
            foreach (var entry in Directory.GetFiles(Path.Combine(options.CacheDirectory, "analysis-v1"), "*.blob")) File.WriteAllText(entry, "corrupt analysis");
            var analysisRecoveryDir = Path.Combine(root, "analysis-recovery"); var analysisRecovery = await service.ExportPlcAsync(analysisRecoveryDir, null, CancellationToken.None, options);
            var recoveredAnalysis = XmlProjectAnalyzer.Analyze(analysisRecoveryDir, analysisRecovery, CancellationToken.None, analysisOptions);
            check(analysisRecovery.Metrics.AnalysisCacheMisses == 6 && recoveredAnalysis.Blocks.Count == 6 && analysisRecovery.Issues.Count == 0,
                "corrupt analysis cache cannot replace or remove current source analysis");
            var bytes = new ContentFileCache(Path.Combine(root, "blobs"));
            var text = Path.Combine(root, "payload"); File.WriteAllText(text, "exact bytes \u00e8\n"); bytes.Save("key", text, CancellationToken.None);
            var restore = Path.Combine(root, "restored"); string warning;
            check(bytes.Restore("key", restore, CancellationToken.None, out warning) && File.ReadAllBytes(text).SequenceEqual(File.ReadAllBytes(restore)), "cache restores byte-for-byte content using a streaming integrity check");
            var data = File.ReadAllBytes(bytes.EntryPath("key")); data[data.Length - 1] ^= 1; File.WriteAllBytes(bytes.EntryPath("key"), data);
            var bad = Path.Combine(root, "bad");
            check(!bytes.Restore("key", bad, CancellationToken.None, out warning) && !File.Exists(bad), "payload corruption removes only the incomplete restored file");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool observed = false;
                try { bytes.Restore("key", bad, cancelled.Token, out warning); } catch (OperationCanceledException) { observed = true; }
                check(observed && !File.Exists(bad), "cancelled cache restore cleans partial output and propagates cancellation");
            }
            int exports = 0, stamps = 0;
            Func<ExportStamp> stable = () => new ExportStamp { ReliableForXml = true, Token = "old", Fingerprints = new List<ObjectFingerprint> { new ObjectFingerprint { Kind = "Code", Value = "old" } } };
            var originalWriter = new PlcExportWriter(Path.Combine(root, "stamp-a"), true, null, CancellationToken.None, options) { ApiIdentity = "test", SourceIdentity = "test" };
            originalWriter.Write("P", "PLC", "Blocks", "FB", "FB", "SCL", false, f => { exports++; File.WriteAllText(f.FullName, "<old/>"); }, readStamp: stable);
            var changedWriter = new PlcExportWriter(Path.Combine(root, "stamp-b"), true, null, CancellationToken.None, options) { ApiIdentity = "test", SourceIdentity = "test" };
            changedWriter.Write("P", "PLC", "Blocks", "FB", "FB", "SCL", false, f => { exports++; File.WriteAllText(f.FullName, "<fresh/>"); }, readStamp: () =>
                new ExportStamp { ReliableForXml = true, Token = ++stamps == 1 ? "old" : "new" });
            check(exports == 2 && !changedWriter.Snapshot.Artifacts[0].ReusedXml && changedWriter.Snapshot.Notes.Count > 0,
                "a fingerprint changed during reuse forces a fresh export");
            var metricsZip = PackageWriter.Build(recoveryDir, recovery, "", new PackageDiagnostics(), CancellationToken.None);
            check(File.Exists(metricsZip) && File.Exists(Path.Combine(recoveryDir, "metrics.json")), "metrics accompany a complete ZIP snapshot");
        }
        finally { await service.ShutdownAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
