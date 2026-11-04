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

internal static class LogicChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-logic-check-" + Guid.NewGuid().ToString("N"));
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var instances = await service.DiscoverAsync(); await service.ConnectAsync(instances[0]);
            var directory = Path.Combine(root, "demo"); var snapshot = await service.ExportPlcAsync(directory, null, CancellationToken.None);
            var analysis = XmlProjectAnalyzer.Analyze(directory, snapshot, CancellationToken.None);
            var trace = ConditionTracer.Trace(analysis, "MotorRun", CancellationToken.None);
            check(trace.Steps.Any(s => s.Description.Contains("SafetyOK") && s.Description.Contains("DriveReady") && s.Description.Contains("JamDetected")),
                "motor trace follows the local permissive assignment to all three interlocks");
            check(trace.Steps.Any(s => s.Block == "Main" && s.Description.Contains("AutoMode")), "motor trace follows the FB input through its instance DB call");
            check(analysis.Assignments.Count(a => a.TargetKey == SymbolIdentity.Key("MotorRun")) == 2, "true and false motor assignments remain distinct guarded paths");
            var selected = FocusPackage.Select(snapshot, analysis, "MotorRun", CancellationToken.None);
            check(selected.Snapshot.Artifacts.Count == 4 && !selected.Snapshot.Artifacts.Any(a => a.Name == "ResetFault" || a.Name == "MotorState"),
                "focused demo includes callers instance FB and tags while omitting unrelated FC and UDT");
            var zip = PackageWriter.Build(directory, snapshot, "Motore fermo", new PackageDiagnostics(), CancellationToken.None, analysis, "MotorRun");
            using (var archive = ZipFile.OpenRead(zip))
            {
                ExportSnapshot manifest; using (var stream = archive.GetEntry("manifest.json").Open()) manifest = JsonFiles.Read<ExportSnapshot>(stream);
                check(manifest.Artifacts.Count == 4 && manifest.Selection.TotalArtifacts == 6 && manifest.Selection.ExcludedIds.Count == 2 &&
                    archive.GetEntry("inventory.json") != null && archive.GetEntry("trace.md") != null, "focused ZIP declares coverage exclusions inventory and condition evidence");
                check(snapshot.Artifacts.Where(a => !manifest.Artifacts.Any(m => m.Id == a.Id)).All(a => archive.GetEntry(a.RelativePath) == null), "excluded XML cannot leak into a focused ZIP");
            }
            var branches = Parse("IF #A THEN #Out := TRUE; ELSIF #B THEN #Out := FALSE; ELSE #Out := TRUE; END_IF;");
            foreach (var row in new[] { new[] { true, true, true }, new[] { true, false, true }, new[] { false, true, false }, new[] { false, false, true } })
            {
                var inputs = new Dictionary<string, bool> { { SymbolIdentity.Key("#A"), row[0] }, { SymbolIdentity.Key("#B"), row[1] } };
                var enabled = branches.Assignments.Where(a => Eval(a.Guard, inputs)).ToList();
                check(enabled.Count == 1 && Eval(enabled[0].Value, inputs) == row[2], "IF ELSIF ELSE guards are exclusive for A=" + row[0] + " B=" + row[1]);
            }
            var nested = Parse("IF #A THEN IF #B THEN #Out := TRUE; END_IF; END_IF;");
            check(!Eval(nested.Assignments[0].Guard, new Dictionary<string, bool> { { SymbolIdentity.Key("#A"), false }, { SymbolIdentity.Key("#B"), true } }),
                "nested IF retains the outer enabling condition");
            var quoted = Parse("\"A.B\" := TRUE; \"A\".B := FALSE; #Text := 'not // a comment';");
            check(quoted.Assignments[0].TargetKey != quoted.Assignments[1].TargetKey && quoted.Assignments[2].Value.Text.Contains("not // a comment"),
                "quoted component boundaries and string literals retain their meaning");
            var call = Parse("\"Worker\"(Enable := #A); #Out := TRUE;");
            check(call.Calls.Count == 1 && call.Assignments.Count == 1 && call.Assignments[0].Target == "#Out", "call parameter association is never treated as an assignment");
            var loop = Parse("#Before := TRUE; FOR #i := 0 TO 4 DO #Out := TRUE; END_FOR;");
            check(!loop.Coverage[0].Supported && loop.Assignments.All(a => !a.Supported), "unsupported loops invalidate coverage rather than creating unconditional writes");
            check(!Parse("#Out := ABS(#Speed) > 5;").Coverage[0].Supported && !Parse("#Text := 'unterminated;").Coverage[0].Supported,
                "unknown expressions and malformed literals cannot be reported as supported logic");
            check(!Parse("%MW0 := 0;").Coverage[0].Supported, "absolute writes flag unmodelled overlapping-address effects");
            var invocation = new ProjectAnalysis { Blocks = new List<BlockAnalysis> {
                new BlockAnalysis { ArtifactId = "caller", Name = "Caller", Project = "P", Plc = "PLC", Unit = "", Kind = "OB", File = "caller.xml",
                    Networks = new List<NetworkEvidence> { new NetworkEvidence { Id = "1", Source = "IF \"Gate\" THEN \"Child\"(); END_IF;", Language = "SCL" } } },
                new BlockAnalysis { ArtifactId = "child", Name = "Child", Project = "P", Plc = "PLC", Unit = "", Kind = "FC", File = "child.xml",
                    Networks = new List<NetworkEvidence> { new NetworkEvidence { Id = "1", Source = "\"MotorRun\" := TRUE;", Language = "SCL" } } } } };
            SclLogicAnalyzer.Parse(invocation, CancellationToken.None); SclLogicAnalyzer.ResolveCalls(invocation);
            check(ConditionTracer.Trace(invocation, "MotorRun", CancellationToken.None).Steps.Any(s => s.Block == "Caller" && s.Description.Contains("Gate")),
                "caller execution guard is traced even when the callee has no input parameters");
            analysis.Coverage.Add(new LogicCoverage { ArtifactId = analysis.Blocks.Single(b => b.Name == "ResetFault").ArtifactId, Network = "unknown", Reason = "Unsupported" });
            check(FocusPackage.Select(snapshot, analysis, "MotorRun", CancellationToken.None).Snapshot.Artifacts.Count == 6,
                "unknown logic causes conservative inclusion of the affected PLC scope");
            bool unknown = false; try { FocusPackage.Select(snapshot, analysis, "DoesNotExist", CancellationToken.None); } catch (InvalidOperationException) { unknown = true; }
            check(unknown, "unknown focus query cannot produce a misleading empty package");
        }
        finally { await service.ShutdownAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static ProjectAnalysis Parse(string source)
    {
        var analysis = new ProjectAnalysis { Blocks = new List<BlockAnalysis> { new BlockAnalysis { ArtifactId = "test", Name = "Test", Kind = "FB",
            Project = "P", Plc = "PLC", Unit = "", Networks = new List<NetworkEvidence> { new NetworkEvidence { Id = "1", Source = source, Language = "SCL" } } } } };
        SclLogicAnalyzer.Parse(analysis, CancellationToken.None); return analysis;
    }
    private static bool Eval(LogicExpression node, IDictionary<string, bool> values)
    {
        switch (node.Kind)
        {
            case "Literal": return string.Equals(node.Text, "TRUE", StringComparison.OrdinalIgnoreCase);
            case "Reference": return values[node.Symbol];
            case "Not": return !Eval(node.Children[0], values);
            case "And": return Eval(node.Children[0], values) && Eval(node.Children[1], values);
            case "Or": return Eval(node.Children[0], values) || Eval(node.Children[1], values);
            default: throw new Exception("Unexpected expression in truth-table test: " + node.Kind);
        }
    }
}
