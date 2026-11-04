using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

internal static class AnalysisChecks
{
    private static readonly XNamespace Scl = "http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v3";
    private static readonly XNamespace Flg = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4";
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-analysis-check-" + Guid.NewGuid().ToString("N"));
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var instances = await service.DiscoverAsync(); await service.ConnectAsync(instances[0]);
            var demoDir = Path.Combine(root, "demo");
            var demo = await service.ExportPlcAsync(demoDir, null, CancellationToken.None);
            var demoAnalysis = XmlProjectAnalyzer.Analyze(demoDir, demo, CancellationToken.None);
            check(demoAnalysis.Dependencies.Any(d => d.TargetName == "ConveyorData" && d.Relation == "Call" && d.Resolution == "Resolved") &&
                demoAnalysis.Dependencies.Any(d => d.TargetName == "ConveyorControl" && d.Relation == "InstanceOf" && d.Resolution == "Resolved"), "SCL instance DB call leads to its FB within the same PLC");
            check(XmlProjectAnalyzer.Search(demoAnalysis, "SafetyOK").Any(s => s.Block == "ConveyorControl" && s.Network == "1") &&
                XmlProjectAnalyzer.Search(demoAnalysis, "%M0.1").Any(s => s.Evidence == "Tag declaration"), "variable search finds logic references and tag addresses");
            var fixtureDir = Path.Combine(root, "fixtures");
            var writer = new PlcExportWriter(fixtureDir, true, null, CancellationToken.None);
            var st = new XElement(Scl + "StructuredText",
                Access("LocalVariable", "Allowed"), Token(":="), Access("LocalConstant", "Limit", true), Token(";"), new XElement(Scl + "NewLine"),
                Access("LocalVariable", "Allowed"), Token(":="), Literal("12", "Int"), Token(";"), new XElement(Scl + "NewLine"),
                new XElement(Scl + "LineComment", new XElement(Scl + "Text", "\"Ghost\"();")), new XElement(Scl + "NewLine"),
                new XElement(Scl + "Access", new XAttribute("Scope", "Call"), new XElement(Scl + "CallInfo", new XAttribute("Name", "FBChild"),
                    new XElement(Scl + "Instance", new XAttribute("Scope", "GlobalVariable"), new XElement(Scl + "Component", new XAttribute("Name", "ChildDB"))),
                    Token("("), new XElement(Scl + "Parameter", new XAttribute("Name", "Enable"), Token(":="), Literal("TRUE", "Bool")), Token(")"))), Token(";"),
                new XElement(Scl + "Access", new XAttribute("Scope", "Call"), new XElement(Scl + "CallInfo",
                    new XElement(Scl + "Instance", new XAttribute("Scope", "LocalVariable"), new XElement(Scl + "Component", new XAttribute("Name", "Worker"))), Token("("), Token(")"))), Token(";"),
                new XElement(Scl + "Text", "\n\"OnlyOtherPlc\"();\n"));
            var document = Document("Caller", "OB", st);
            document.Descendants("AttributeList").First().Add(new XElement("Interface", new XElement("Sections", new XElement("Section", new XAttribute("Name", "Static"),
                new XElement("Member", new XAttribute("Name", "Worker"), new XAttribute("Datatype", "FBChild")),
                new XElement("Member", new XAttribute("Name", "State"), new XAttribute("Datatype", "\"MotorState\""))))));
            var graph = new XElement(Flg + "FlgNet", new XElement(Flg + "Parts", new XElement(Flg + "Part", new XAttribute("Name", "Contact"),
                new XAttribute("UId", "50"), new XElement(Flg + "Negated", new XAttribute("Name", "operand")))),
                new XElement(Flg + "Wires", new XElement(Flg + "Wire", new XAttribute("UId", "51"), new XElement(Flg + "Powerrail"), new XElement(Flg + "NameCon", new XAttribute("UId", "50"), new XAttribute("Name", "in")))));
            document.Root.Element("SW.Blocks.OB").Element("ObjectList").Add(new XElement("SW.Blocks.CompileUnit", new XAttribute("ID", "2"), new XElement("AttributeList", new XElement("NetworkSource", graph))));
            Add(writer, "PLC_A", "Blocks", "Caller", "OB", document);
            Add(writer, "PLC_A", "Blocks", "FBChild", "FB", Document("FBChild", "FB", new XElement(Scl + "StructuredText")));
            Add(writer, "PLC_A", "Types", "MotorState", "UDT", Document("MotorState", "UDT", new XElement("DataOnly")));
            Add(writer, "PLC_B", "Blocks", "FBChild", "FB", Document("FBChild", "FB", new XElement(Scl + "StructuredText")));
            Add(writer, "PLC_B", "Blocks", "OnlyOtherPlc", "FC", Document("OnlyOtherPlc", "FC", new XElement(Scl + "StructuredText")));
            Add(writer, "PLC_A", "Units/First/Blocks", "UnitCaller", "OB", Document("UnitCaller", "OB", Call("FBChild")));
            Add(writer, "PLC_A", "Units/First/Blocks", "FBChild", "FB", Document("FBChild", "FB", new XElement(Scl + "StructuredText")));
            Add(writer, "PLC_A", "Units/Second/Blocks", "FBChild", "FB", Document("FBChild", "FB", new XElement(Scl + "StructuredText")));
            Add(writer, "PLC_A", "Blocks/A", "Duplicate", "FC", Document("Duplicate", "FC", new XElement(Scl + "StructuredText")));
            Add(writer, "PLC_A", "Blocks/B", "Duplicate", "FC", Document("Duplicate", "FC", new XElement(Scl + "StructuredText")));
            Add(writer, "PLC_A", "Blocks", "AmbiguousCaller", "OB", Document("AmbiguousCaller", "OB", Call("Duplicate")));
            var idb = Document("ChildDB", "InstanceDB", new XElement("UnknownNetwork"));
            idb.Descendants("AttributeList").First().Add(new XElement("InstanceOfName", "FBChild"));
            Add(writer, "PLC_A", "Blocks", "ChildDB", "InstanceDB", idb);
            writer.Write("P", "PLC_A", "Blocks", "RejectedDtd", "FB", "SCL", false,
                f => File.WriteAllText(f.FullName, "<!DOCTYPE Document [<!ENTITY x SYSTEM 'file:///must-not-read'>]><Document>&x;</Document>"));
            var analysis = XmlProjectAnalyzer.Analyze(fixtureDir, writer.Snapshot, CancellationToken.None);
            var caller = analysis.Blocks.Single(b => b.Name == "Caller");
            check(caller.Networks[0].Source.Contains("#Allowed:=#Limit;") && caller.Networks[0].Source.Contains("#Allowed:=12;"), "documented SCL local constants and literal values are reconstructed without type-name contamination");
            check(caller.Networks[0].Source.Contains("\"ChildDB\"(Enable:=TRUE)") && caller.Networks[0].Source.Contains("#Worker()"), "SCL calls retain instance names and parameter bindings");
            check(caller.Networks[1].GraphXml.Contains("Negated") && caller.Networks[1].GraphXml.Contains("Wire"), "LAD topology and negated operands are preserved verbatim");
            check(!analysis.Dependencies.Any(d => d.TargetName == "Ghost"), "XML comments do not create fake calls");
            check(analysis.Dependencies.Any(d => d.TargetName == "OnlyOtherPlc" && d.Resolution == "ExternalOrUnresolved"), "same-named blocks in another PLC never satisfy a call");
            var unitCaller = analysis.Blocks.Single(b => b.Name == "UnitCaller");
            var unitDependency = analysis.Dependencies.Single(d => d.FromId == unitCaller.ArtifactId);
            check(analysis.Blocks.Single(b => b.ArtifactId == unitDependency.TargetId).Unit == "First", "software unit calls resolve only within their unit");
            check(analysis.Dependencies.Any(d => d.TargetName == "Duplicate" && d.Resolution == "Ambiguous" && d.TargetId == null), "ambiguous dependencies remain unresolved");
            check(analysis.Dependencies.Any(d => d.Relation == "InstanceOf" && d.TargetName == "FBChild" && d.Resolution == "Resolved"), "instance DB dependency is linked to its FB");
            check(analysis.Dependencies.Any(d => d.Relation == "Type" && d.TargetName == "MotorState" && d.Resolution == "Resolved"), "interface datatype reference resolves to the PLC UDT");
            check(analysis.Warnings.Any(w => w.Contains("RejectedDtd")), "DTD and external entities are rejected and reported without aborting other blocks");
            var masked = XmlProjectAnalyzer.MaskCommentsAndStrings("(* nested (* \"Fake\"(); *) *)\n'\"Fake\"()'\n\"Real//name\"(); // \"Fake\"()");
            check(!masked.Contains("Fake") && masked.Contains("Real//name"), "SCL masking respects nested comments strings and quoted identifiers");
            await ComparisonTests(root, demoDir, demo, service, check);
        }
        finally { await service.ShutdownAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static async Task ComparisonTests(string root, string beforeDir, ExportSnapshot before, TiaConnectionService service, Action<bool, string> check)
    {
        SnapshotStore.Complete(beforeDir, before, CancellationToken.None);
        var afterDir = Path.Combine(root, "after");
        var after = await service.ExportPlcAsync(afterDir, null, CancellationToken.None);
        var motor = after.Artifacts.Single(a => a.Name == "ConveyorControl");
        var motorPath = ExportPaths.Resolve(afterDir, motor.RelativePath);
        File.WriteAllText(motorPath, File.ReadAllText(motorPath).Replace("AND NOT", "OR NOT"));
        var main = after.Artifacts.Single(a => a.Name == "Main");
        var mainPath = ExportPaths.Resolve(afterDir, main.RelativePath);
        var doc = XDocument.Load(mainPath); doc.Root.AddFirst(new XElement("DocumentInfo", new XElement("ExportTime", "2099-01-01"))); doc.Save(mainPath);
        var removed = after.Artifacts.Single(a => a.Name == "MotorState"); after.Artifacts.Remove(removed);
        var extra = new ExportArtifact { Id = ExportPaths.Hash("new-block"), Project = "DEMO Linea confezionamento", Plc = "PLC_Conveyor", Group = "Blocks", Name = "NewBlock", Kind = "FC", Status = "Exported", RelativePath = "new.xml" };
        File.WriteAllText(ExportPaths.Resolve(afterDir, extra.RelativePath), "<Document/>"); after.Artifacts.Add(extra);
        XmlProjectAnalyzer.Analyze(afterDir, after, CancellationToken.None);
        var zip = PackageWriter.Build(afterDir, after, "", new PackageDiagnostics(), CancellationToken.None);
        var comparison = SnapshotComparer.Compare(Path.Combine(beforeDir, "manifest.json"), zip);
        check(comparison.Changes.Any(c => c.Name == "ConveyorControl" && c.Change == "Changed") && comparison.Changes.Any(c => c.Name == "Main" && c.Change == "ExportMetadataOnly"), "comparison distinguishes changed logic from document export metadata");
        check(comparison.Changes.Any(c => c.Name == "MotorState" && c.Change == "Removed") && comparison.Changes.Any(c => c.Name == "NewBlock" && c.Change == "Added"), "comparison reports added and removed artifacts");
        check(comparison.Unchanged == 3, "unchanged hashes are not reported as modifications");
        var opposite = SnapshotComparer.ReadManifest(zip); opposite.IsDemo = false;
        var invalidPath = Path.Combine(root, "opposite.json"); JsonFiles.Write(invalidPath, opposite);
        bool rejected = false; try { SnapshotComparer.Compare(invalidPath, zip); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "comparison refuses mixing synthetic and real snapshots");
        var duplicatePath = Path.Combine(root, "duplicate.json"); opposite.Artifacts.Add(opposite.Artifacts[0]); JsonFiles.Write(duplicatePath, opposite);
        rejected = false; try { SnapshotComparer.ReadManifest(duplicatePath); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "duplicate manifest artifact identities are rejected");
        var malformed = Path.Combine(root, "malformed.json"); File.WriteAllText(malformed, "{}"); rejected = false;
        try { SnapshotComparer.ReadManifest(malformed); } catch (Exception ex) when (ex is InvalidDataException || ex is System.Runtime.Serialization.SerializationException) { rejected = true; }
        check(rejected, "missing required provenance and schema are rejected");
    }
    private static XElement Token(string text) { return new XElement(Scl + "Token", new XAttribute("Text", text)); }
    private static XElement Access(string scope, string name, bool constant = false)
    {
        return new XElement(Scl + "Access", new XAttribute("Scope", scope), constant ? new XElement(Scl + "Constant", new XAttribute("Name", name)) :
            new XElement(Scl + "Symbol", new XElement(Scl + "Component", new XAttribute("Name", name))));
    }
    private static XElement Literal(string value, string type) { return new XElement(Scl + "Access", new XAttribute("Scope", "LiteralConstant"), new XElement(Scl + "Constant", new XElement(Scl + "ConstantType", type), new XElement(Scl + "ConstantValue", value))); }
    private static XElement Call(string target) { return new XElement(Scl + "StructuredText", new XElement(Scl + "Access", new XAttribute("Scope", "Call"), new XElement(Scl + "CallInfo", new XAttribute("Name", target), Token("("), Token(")")))); }
    private static XDocument Document(string name, string kind, XElement source)
    {
        return new XDocument(new XElement("Document", new XElement("SW.Blocks." + kind, new XElement("AttributeList", new XElement("Name", name)),
            new XElement("ObjectList", new XElement("SW.Blocks.CompileUnit", new XAttribute("ID", "1"), new XElement("AttributeList", new XElement("NetworkSource", source)))))));
    }
    private static void Add(PlcExportWriter writer, string plc, string group, string name, string kind, XDocument document)
    { writer.Write("P", plc, group, name, kind, "SCL", false, file => document.Save(file.FullName)); }
}
