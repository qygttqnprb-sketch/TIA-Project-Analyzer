using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;

internal static class GraphChecks
{
    private static readonly XNamespace Ns = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4";
    public static void Run(Action<bool, string> check)
    {
        var series = Series();
        var analysis = Parse(series, "LAD");
        check(analysis.Coverage.Single().Supported && analysis.Assignments.Count == 1, "LAD series yields a supported coil assignment");
        var assignment = analysis.Assignments.Single();
        for (int row = 0; row < 8; row++)
        {
            bool auto = (row & 1) != 0, safe = (row & 2) != 0, jam = (row & 4) != 0;
            check(Eval(assignment.Value, new Dictionary<string, bool> { { "Auto", auto }, { "SafetyOK", safe }, { "JamDetected", jam } }) == (auto && safe && !jam),
                "LAD truth table preserves series and NC contact, row " + row);
        }
        var parallel = new Graph();
        parallel.Variable("1", "Auto"); parallel.Variable("2", "Manual"); parallel.Variable("3", "SafetyOK"); parallel.Variable("4", "MotorRun");
        parallel.Part("10", "Contact"); parallel.Part("11", "Contact"); parallel.Part("12", "Contact"); parallel.Part("13", "Coil");
        parallel.Wire(Rail(), Pin("10", "in"), Pin("11", "in"));
        parallel.Wire(Ident("1"), Pin("10", "operand")); parallel.Wire(Ident("2"), Pin("11", "operand")); parallel.Wire(Ident("3"), Pin("12", "operand"));
        parallel.Wire(Pin("10", "out"), Pin("11", "out"), Pin("12", "in"));
        parallel.Wire(Pin("12", "out"), Pin("13", "in")); parallel.Wire(Ident("4"), Pin("13", "operand"));
        var branches = Parse(parallel, "LAD");
        check(branches.Coverage.Single().Supported && Eval(branches.Assignments.Single().Value,
            new Dictionary<string, bool> { { "Auto", false }, { "Manual", true }, { "SafetyOK", true } }), "LAD parallel branches merge with OR before series interlock");
        check(!Eval(branches.Assignments.Single().Value,
            new Dictionary<string, bool> { { "Auto", true }, { "Manual", true }, { "SafetyOK", false } }), "LAD parallel branch cannot bypass downstream safety contact");
        var fbd = Gate("A", true);
        var boolean = Parse(fbd, "FBD");
        check(boolean.Coverage.Single().Supported && Eval(boolean.Assignments.Single().Value,
            new Dictionary<string, bool> { { "Auto", true }, { "JamDetected", false } }), "FBD AND uses cardinality and negated input pins");
        var inverted = new Graph(fbd.Xml);
        inverted.Xml.Descendants(Ns + "Part").Single(e => (string)e.Attribute("Name") == "A").Add(new XElement(Ns + "Negated", new XAttribute("Name", "out")));
        check(!Eval(Parse(inverted, "FBD").Assignments.Single().Value,
            new Dictionary<string, bool> { { "Auto", true }, { "JamDetected", false } }), "FBD negated gate output is retained");
        var disjunction = Parse(Gate("O", false), "FBD");
        check(Eval(disjunction.Assignments.Single().Value, new Dictionary<string, bool> { { "Auto", false }, { "JamDetected", true } }), "FBD OR direct output is a write site");
        foreach (var kind in new[] { "A", "O" })
            for (int row = 0; row < 4; row++)
            {
                bool left = (row & 1) != 0, right = (row & 2) != 0;
                var gate = Parse(Gate(kind, false), "FBD");
                check(Eval(gate.Assignments.Single().Value, new Dictionary<string, bool> { { "Auto", left }, { "JamDetected", right } }) ==
                    (kind == "A" ? left && right : left || right), "FBD " + kind + " truth table, row " + row);
            }
        var inverseCoil = Series();
        inverseCoil.Xml.Descendants(Ns + "Part").Single(e => (string)e.Attribute("Name") == "Coil").Add(new XElement(Ns + "Negated", new XAttribute("Name", "operand")));
        check(Eval(Parse(inverseCoil, "LAD").Assignments.Single().Value,
            new Dictionary<string, bool> { { "Auto", false }, { "SafetyOK", true }, { "JamDetected", false } }), "negated coil inverts assignment rather than its enabling condition");
        var stateful = new Graph();
        stateful.Variable("1", "Start"); stateful.Variable("2", "Stop"); stateful.Variable("3", "MotorRun");
        stateful.Part("10", "SCoil"); stateful.Part("11", "RCoil");
        stateful.Wire(Ident("1"), Pin("10", "in")); stateful.Wire(Ident("2"), Pin("11", "in"));
        stateful.Wire(Ident("3"), Pin("10", "operand")); stateful.Wire(Ident("3"), Pin("11", "operand"));
        var latch = Parse(stateful, "FBD");
        check(latch.Coverage.Single().Supported && latch.Assignments.Select(a => a.Operation).SequenceEqual(new[] { "Set", "Reset" }) &&
            latch.Assignments[1].Value.Text == "FALSE", "set and reset coils retain distinct conditional write operations");
        check(!Eval(latch.Assignments[0].Guard, new Dictionary<string, bool> { { "Start", false } }), "disabled set coil does not become an unconditional TRUE write");
        var unsupported = Series(); unsupported.Part("90", "TON");
        var partial = Parse(unsupported, "LAD");
        check(!partial.Coverage.Single().Supported && partial.Assignments.Count == 1 && !partial.Assignments[0].Supported,
            "unsupported timer marks known neighboring writes as partial without deleting them");
        var missing = Series(); missing.Xml.Descendants(Ns + "Wire").First().Remove();
        check(!Parse(missing, "LAD").Coverage.Single().Supported, "missing power input cannot be inferred as TRUE");
        var duplicate = Series(); duplicate.Xml.Element(Ns + "Parts").Add(new XElement(duplicate.Xml.Element(Ns + "Parts").Elements().First()));
        check(!Parse(duplicate, "LAD").Coverage.Single().Supported, "duplicate graph UIds are rejected as incomplete coverage");
        var version = Series(); version.Xml.Name = XName.Get("FlgNet", Ns.NamespaceName.Replace("v4", "v99"));
        check(!Parse(version, "LAD").Coverage.Single().Supported, "unknown graph schema is not interpreted using guessed semantics");
        var foreign = Series(); foreign.Xml.Element(Ns + "Parts").Add(new XElement("foreign"));
        check(!Parse(foreign, "LAD").Coverage.Single().Supported, "mixed namespaces are rejected");
        var indexed = Series(); indexed.Xml.Descendants(Ns + "Component").First().Add(new XElement(Ns + "Access", new XAttribute("Scope", "LiteralConstant")));
        check(!Parse(indexed, "LAD").Coverage.Single().Supported, "indexed graphical operands remain explicitly unsupported");
        var cycle = Gate("A", false);
        cycle.Xml.Descendants(Ns + "Wire").First(w => w.Elements(Ns + "NameCon").Any(p => (string)p.Attribute("Name") == "in1")).Remove();
        cycle.Xml.Descendants(Ns + "Wire").Single(w => w.Elements(Ns + "NameCon").Any(p => (string)p.Attribute("Name") == "out")).Add(Pin("10", "in1"));
        check(!Parse(cycle, "FBD").Coverage.Single().Supported, "graph feedback cycle terminates with partial coverage");
        var invalid = Gate("A", false); invalid.Xml.Descendants(Ns + "TemplateValue").Single().Value = "99999";
        check(!Parse(invalid, "FBD").Coverage.Single().Supported, "gate cardinality bounds prevent oversized evaluation");
        var orphan = Series(); orphan.Wire(Ident("1"), Ident("2"));
        check(!Parse(orphan, "LAD").Coverage.Single().Supported, "unconsumed ambiguous wiring cannot be silently ignored");
        var call = new Graph(); call.Variable("1", "MotorRun");
        call.Xml.Element(Ns + "Parts").Add(new XElement(Ns + "Call", new XAttribute("UId", "10"), new XElement(Ns + "CallInfo",
            new XAttribute("Name", "Helper"), new XElement(Ns + "Parameter", new XAttribute("Name", "Result"), new XAttribute("Section", "Output")))));
        call.Wire(Pin("10", "Result"), Ident("1"));
        var output = Parse(call, "FBD");
        check(!output.Coverage.Single().Supported && output.Assignments.Single().Operation == "CallOutput" &&
            !output.Assignments.Single().Supported && output.Assignments.Single().Value.Kind == "Unknown",
            "typed graphical call outputs retain partial write evidence without invented values");
        var malformed = Gate("A", false);
        var access = malformed.Xml.Element(Ns + "Parts").Elements().First();
        access.SetAttributeValue("Scope", "LiteralConstant"); access.RemoveNodes();
        access.Add(new XElement(Ns + "Constant"), new XElement(Ns + "Constant"));
        check(!Parse(malformed, "FBD").Coverage.Single().Supported, "malformed literal structure produces partial coverage rather than crashing project analysis");
        check(!Parse(parallel, "FBD").Coverage.Single().Supported, "LAD multiple-driver interpretation is never applied to FBD");
        WriteChecks(latch, check);
        IntegratedChecks(series, check);
        bool cancelled = false;
        try { GraphLogicAnalyzer.Parse(new ProjectAnalysis(), new BlockAnalysis(), new NetworkEvidence(), new XElement("NetworkSource"), new CancellationToken(true)); }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "graph analysis propagates cancellation");
    }
    private static void WriteChecks(ProjectAnalysis latch, Action<bool, string> check)
    {
        var analysis = new ProjectAnalysis();
        AddScl(analysis, "A", "PLC", "\"MotorRun\" := TRUE; #Temp := FALSE;");
        AddScl(analysis, "B", "PLC", "IF #Fault THEN \"MotorRun\" := FALSE; END_IF; #Temp := TRUE;");
        AddScl(analysis, "C", "OTHER", "\"MotorRun\" := FALSE;");
        SclLogicAnalyzer.Parse(analysis, CancellationToken.None);
        analysis.Writes = WriteAnalyzer.Analyze(analysis, CancellationToken.None);
        var group = analysis.Writes.Groups.Single(g => g.Plc == "PLC" && g.TargetKey == SymbolIdentity.Key("MotorRun"));
        check(group.Classification == "MultipleBlocks" && group.Sites.Count == 2 && group.Sites.Any(s => s.Value == "FALSE" && s.Guard.Contains("Fault")),
            "writer index identifies a guarded reset in another block");
        check(analysis.Writes.Groups.Count(g => g.TargetKey == SymbolIdentity.Key("MotorRun")) == 2, "same-named globals in different PLCs remain separate");
        check(analysis.Writes.Groups.Count(g => g.TargetKey == SymbolIdentity.Key("#Temp")) == 2 &&
            analysis.Writes.Groups.Where(g => g.LocalBlockId != null).All(g => g.Sites.Count == 1), "same-named locals in different blocks cannot become false multiple writers");
        var exclusive = new ProjectAnalysis(); AddScl(exclusive, "A", "PLC", "IF #Auto THEN \"MotorRun\" := TRUE; ELSE \"MotorRun\" := FALSE; END_IF;");
        SclLogicAnalyzer.Parse(exclusive, CancellationToken.None);
        var report = WriteAnalyzer.Analyze(exclusive, CancellationToken.None);
        check(report.Groups.Single().Classification == "MultipleSites" && report.Warnings.Any(w => w.Contains("non dimostrano un conflitto")),
            "exclusive branch sites are not classified as proven conflicts");
        var native = new PlcCrossReference { Project = "P", Plc = "PLC", Provider = "Siemens V21", SourceName = "MotorRun",
            ReferencePath = "A", Location = "network 1", Access = "Write", QueriedArtifactId = "A" };
        analysis.CrossReferences.Add(native);
        analysis.CrossReferences.Add(new PlcCrossReference { Project = "P", Plc = "PLC", Provider = "Siemens V21", SourceName = "MotorRun",
            ReferencePath = "A", Location = "network 1", Access = "Write", QueriedArtifactId = "B" });
        analysis.CrossReferences.Add(new PlcCrossReference { Access = "Read", SourceName = "MotorRun" });
        var withNative = WriteAnalyzer.Analyze(analysis, CancellationToken.None);
        check(withNative.NativeWrites.Count == 1 && withNative.Groups.Single(g => g.ScopeKey == group.ScopeKey).Sites.Count == 2,
            "native write rows are deduplicated and never added to XML site counts");
        check(WriteAnalyzer.Select(withNative, "MotorRun").Groups.Count == 2 && WriteAnalyzer.Select(withNative, "absent").Groups.Count == 0,
            "writer lookup supports selected symbols and missing queries");
        var latchReport = WriteAnalyzer.Analyze(latch, CancellationToken.None);
        check(latchReport.Groups.Single().Sites.Count == 2 && WriteAnalyzer.Markdown(latchReport).Contains("Reset"), "set/reset sites are included in the human-readable writer report");
        check(WriteAnalyzer.Markdown(withNative, 1).Contains("Anteprima limitata"), "bounded writer previews retain a link to the full report");
    }
    private static void IntegratedChecks(Graph graph, Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-graph-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, "snapshot");
            Directory.CreateDirectory(directory);
            var snapshot = new ExportSnapshot { IsDemo = true, Artifacts = new List<ExportArtifact> { new ExportArtifact {
                Id = ExportPaths.Identity("P", "PLC", "Motor"), Project = "P", Plc = "PLC", Group = "Blocks", Name = "Motor",
                Kind = "FB", Language = "LAD", Status = "Exported", RelativePath = "motor.xml" } } };
            new XDocument(new XElement("Document", new XElement("SW.Blocks.FB", new XElement("AttributeList", new XElement("Name", "Motor")),
                    new XElement("ObjectList", new XElement("SW.Blocks.CompileUnit", new XAttribute("ID", "1"), new XElement("AttributeList",
                        new XElement("ProgrammingLanguage", "LAD"), new XElement("NetworkSource", new XElement(graph.Xml)))))))).Save(Path.Combine(directory, "motor.xml"));
            var options = new AnalysisOptions { CacheDirectory = Path.Combine(root, "cache"), IncludeNetworkXml = false };
            var first = XmlProjectAnalyzer.Analyze(directory, snapshot, CancellationToken.None, options);
            check(first.Blocks.Single().Networks.Single().GraphXml == null && first.Coverage.Single().Supported && first.Assignments.Count == 1,
                "compact reports parse LAD directly from original XML without retaining a duplicate graph");
            var warm = XmlProjectAnalyzer.Analyze(directory, snapshot, CancellationToken.None, options);
            check(snapshot.Metrics.AnalysisCacheHits == 1 && warm.Assignments.Count == 1 && warm.Writes.Groups.Single().Sites.Count == 1,
                "graph evidence survives analysis cache restore and writer counts are recomputed");
            var trace = ConditionTracer.Trace(warm, "MotorRun", CancellationToken.None);
            check(trace.Steps.Any(s => s.Description.Contains("JamDetected")), "graphical coil conditions participate in symbol traces");
            var traceText = ConditionTracer.Markdown(trace);
            check(traceText.Contains("LAD modellato") && traceText.Contains("UId 13") && !traceText.Contains("riga SCL ricostruita 0"),
                "graph trace cites actual part UID rather than inventing an SCL source line");
            var files = AnalysisWriter.Write(directory, warm, CancellationToken.None);
            WriteReport report; using (var stream = File.OpenRead(Path.Combine(directory, "writers.json"))) report = JsonFiles.Read<WriteReport>(stream);
            check(files.Contains("writers.md") && report.Groups.Single().Sites.Single().Language == "LAD" &&
                File.ReadAllText(Path.Combine(directory, "conditions.md")).Contains("LAD modellato"), "graph writer and conditions reports retain language and source provenance");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void AddScl(ProjectAnalysis analysis, string id, string plc, string source)
    {
        analysis.Blocks.Add(new BlockAnalysis { ArtifactId = id, Name = id, Project = "P", Plc = plc, Unit = "", Kind = "FB", File = id + ".xml",
            Networks = new List<NetworkEvidence> { new NetworkEvidence { Id = "1", Language = "SCL", Source = source } } });
    }
    private static ProjectAnalysis Parse(Graph graph, string language)
    {
        var result = new ProjectAnalysis(); var block = new BlockAnalysis { ArtifactId = "graph", Name = "Motor", Kind = "FB", Project = "P", Plc = "PLC", Unit = "", File = "motor.xml" };
        var network = new NetworkEvidence { Id = "1", Language = language }; block.Networks.Add(network); result.Blocks.Add(block);
        GraphLogicAnalyzer.Parse(result, block, network, new XElement("NetworkSource", new XElement(graph.Xml)), CancellationToken.None);
        return result;
    }
    private static bool Eval(LogicExpression expression, IDictionary<string, bool> values)
    {
        switch (expression.Kind)
        {
            case "Literal": return expression.Text == "TRUE";
            case "Reference": return values.First(v => string.Equals(v.Key, SymbolIdentity.Name(expression.Symbol), StringComparison.OrdinalIgnoreCase)).Value;
            case "And": return Eval(expression.Children[0], values) && Eval(expression.Children[1], values);
            case "Or": return Eval(expression.Children[0], values) || Eval(expression.Children[1], values);
            case "Not": return !Eval(expression.Children[0], values);
            default: throw new InvalidOperationException("Unsupported test expression: " + expression.Kind);
        }
    }
    private static Graph Series()
    {
        var graph = new Graph();
        graph.Variable("1", "Auto"); graph.Variable("2", "SafetyOK"); graph.Variable("3", "JamDetected"); graph.Variable("4", "MotorRun");
        graph.Part("10", "Contact"); graph.Part("11", "Contact"); graph.Part("12", "Contact", "operand"); graph.Part("13", "Coil");
        graph.Wire(Rail(), Pin("10", "in"));
        graph.Wire(Ident("1"), Pin("10", "operand")); graph.Wire(Ident("2"), Pin("11", "operand")); graph.Wire(Ident("3"), Pin("12", "operand"));
        graph.Wire(Pin("10", "out"), Pin("11", "in")); graph.Wire(Pin("11", "out"), Pin("12", "in"));
        graph.Wire(Pin("12", "out"), Pin("13", "in")); graph.Wire(Ident("4"), Pin("13", "operand")); return graph;
    }
    private static Graph Gate(string name, bool negate)
    {
        var graph = new Graph(); graph.Variable("1", "Auto"); graph.Variable("2", "JamDetected"); graph.Variable("3", "MotorRun");
        graph.Part("10", name, negate ? "in2" : null, 2);
        graph.Wire(Ident("1"), Pin("10", "in1")); graph.Wire(Ident("2"), Pin("10", "in2")); graph.Wire(Pin("10", "out"), Ident("3")); return graph;
    }
    private static XElement Pin(string id, string name) { return new XElement(Ns + "NameCon", new XAttribute("UId", id), new XAttribute("Name", name)); }
    private static XElement Ident(string id) { return new XElement(Ns + "IdentCon", new XAttribute("UId", id)); }
    private static XElement Rail() { return new XElement(Ns + "Powerrail"); }
    private sealed class Graph
    {
        public readonly XElement Xml;
        private int wire = 10000;
        public Graph() { Xml = new XElement(Ns + "FlgNet", new XElement(Ns + "Parts"), new XElement(Ns + "Wires")); }
        public Graph(XElement xml) { Xml = new XElement(xml); }
        public void Variable(string id, string name) { Xml.Element(Ns + "Parts").Add(new XElement(Ns + "Access", new XAttribute("UId", id), new XAttribute("Scope", "GlobalVariable"),
            new XElement(Ns + "Symbol", new XElement(Ns + "Component", new XAttribute("Name", name))))); }
        public void Part(string id, string name, string negate = null, int cardinality = 0)
        {
            Xml.Element(Ns + "Parts").Add(new XElement(Ns + "Part", new XAttribute("UId", id), new XAttribute("Name", name),
                negate == null ? null : new XElement(Ns + "Negated", new XAttribute("Name", negate)),
                cardinality == 0 ? null : new XElement(Ns + "TemplateValue", new XAttribute("Name", "Card"), new XAttribute("Type", "Cardinality"), cardinality)));
        }
        public void Wire(params XElement[] ends) { Xml.Element(Ns + "Wires").Add(new XElement(Ns + "Wire", new XAttribute("UId", wire++), ends)); }
    }
}
