using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaAnalyzer.Openness
{
    internal sealed class DemoDocument
    {
        public string Name, Kind, Group, Language, Xml;
    }
    internal static class DemoDocuments
    {
        public static IEnumerable<DemoDocument> Create()
        {
            yield return Block("Main", "OB", "Blocks", "SCL", "\"ConveyorControl\"(Auto := \"AutoMode\");");
            yield return Block("ConveyorControl", "FB", "Blocks/Conveyor", "SCL",
                "// DEMO: dati sintetici, nessun valore runtime\n" +
                "#Permissive := \"SafetyOK\" AND \"DriveReady\" AND NOT \"JamDetected\";\n" +
                "IF #Auto AND #Permissive THEN\n    \"MotorRun\" := TRUE;\nELSE\n    \"MotorRun\" := FALSE;\nEND_IF;");
            yield return Block("ResetFault", "FC", "Blocks/Conveyor", "SCL", "IF \"Reset\" THEN \"Fault\" := FALSE; END_IF;");
            yield return Block("ConveyorData", "GlobalDB", "Blocks", "DB", null);
            yield return Block("MotorState", "UDT", "Types", "", null);
            yield return new DemoDocument { Name = "ConveyorTags", Kind = "TagTable", Group = "Tags", Language = "",
                Xml = new XDocument(new XElement("Document", new XElement("SW.Tags.PlcTagTable",
                    new XElement("AttributeList", new XElement("Name", "ConveyorTags")),
                    new XElement("ObjectList", Tags())))).ToString() };
        }
        private static IEnumerable<XElement> Tags()
        {
            int index = 0;
            foreach (var name in new[] { "AutoMode", "SafetyOK", "DriveReady", "JamDetected", "MotorRun", "Reset", "Fault" })
                yield return new XElement("SW.Tags.PlcTag", new XElement("AttributeList", new XElement("Name", name),
                    new XElement("DataTypeName", "Bool"), new XElement("LogicalAddress", "%M0." + index++)));
        }
        private static DemoDocument Block(string name, string kind, string group, string language, string source)
        {
            XNamespace scl = "http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v3";
            XNamespace iface = "http://www.siemens.com/automation/Openness/SW/Interface/v5";
            var attributes = new XElement("AttributeList", new XElement("Name", name), new XElement("ProgrammingLanguage", language),
                new XElement("Interface", new XElement(iface + "Sections", new XElement(iface + "Section", new XAttribute("Name", "Static"),
                    new XElement(iface + "Member", new XAttribute("Name", "Permissive"), new XAttribute("Datatype", "Bool"))))));
            var block = new XElement(kind == "UDT" ? "SW.Types.PlcStruct" : "SW.Blocks." + kind, attributes);
            if (source != null) block.Add(new XElement("ObjectList", new XElement("SW.Blocks.CompileUnit", new XAttribute("ID", "1"),
                new XElement("AttributeList", new XElement("NetworkSource", new XElement(scl + "StructuredText", new XElement(scl + "Text", source)))))));
            return new DemoDocument { Name = name, Kind = kind, Group = group, Language = language, Xml = new XDocument(new XElement("Document", block)).ToString() };
        }
    }
}
