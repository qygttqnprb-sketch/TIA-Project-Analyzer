using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    public static class AnalysisWriter
    {
        public static IReadOnlyList<string> Write(string directory, ProjectAnalysis analysis, CancellationToken cancellation)
        {
            var files = new List<string> { "analysis.json", "symbols.json", "dependencies.dot", "logic.md", "conditions.json", "conditions.md" };
            JsonFiles.Write(ExportPaths.Resolve(directory, "analysis.json"), analysis);
            JsonFiles.Write(ExportPaths.Resolve(directory, "symbols.json"), analysis.Symbols);
            JsonFiles.Write(ExportPaths.Resolve(directory, "conditions.json"), analysis.Assignments);
            var conditions = new StringBuilder("# Assegnazioni e condizioni SCL\n\nEvidenze offline del sottoinsieme supportato. Non sono valori runtime. Copertura completa in analysis.json.\n\n");
            var byId = analysis.Blocks.ToDictionary(b => b.ArtifactId);
            foreach (var assignment in analysis.Assignments)
            {
                cancellation.ThrowIfCancellationRequested(); var block = byId[assignment.ArtifactId];
                conditions.Append("## ").AppendLine(PackageWriter.Text(block.Plc + " / " + block.Name + " / rete " + assignment.Network + " / riga " + assignment.Line));
                conditions.AppendLine(assignment.Supported ? "\nSCL modellato.\n" : "\n**PARZIALE: leggere la rete completa.**\n");
                conditions.AppendLine(Code(assignment.Target + " := " + assignment.Value.Text + ";\n// QUANDO " + assignment.Guard.Text, "scl"));
                conditions.Append("Fonte XML: ").AppendLine(PackageWriter.Text(block.File));
            }
            foreach (var coverage in analysis.Coverage.Where(c => !c.Supported))
                conditions.Append("\n- NON MODELLATO: ").Append(PackageWriter.Text(byId[coverage.ArtifactId].Name + " / " + coverage.Network)).Append(": ").AppendLine(PackageWriter.Text(coverage.Reason));
            PackageWriter.WriteText(directory, "conditions.md", conditions.ToString());
            var graph = new StringBuilder("digraph PLC {\n  rankdir=LR;\n");
            var report = new StringBuilder("# Evidenze della logica PLC\n\n");
            foreach (var warning in analysis.Warnings) report.Append("- ").AppendLine(PackageWriter.Text(warning));
            report.AppendLine("\n## Blocchi e reti\n");
            Directory.CreateDirectory(Path.Combine(directory, "blocks"));
            foreach (var block in analysis.Blocks)
            {
                cancellation.ThrowIfCancellationRequested();
                var relative = "blocks/" + block.ArtifactId + ".md";
                files.Add(relative);
                graph.Append("  b").Append(block.ArtifactId).Append(" [label=\"").Append(Dot(block.Plc + "/" + block.Name)).AppendLine("\"];");
                report.Append("- [").Append(PackageWriter.Text(block.Name)).Append("](").Append(relative).Append(") · ")
                    .Append(block.Networks.Count).AppendLine(" reti");
                var doc = new StringBuilder("# " + PackageWriter.Text(block.Name) + "\n\n");
                doc.Append("Progetto: ").Append(PackageWriter.Text(block.Project)).Append(". PLC: ").Append(PackageWriter.Text(block.Plc)).AppendLine(".");
                doc.Append("\nXML originale: [").Append(PackageWriter.Text(block.File)).Append("](../")
                    .Append(string.Join("/", block.File.Split('/').Select(Uri.EscapeDataString))).AppendLine(")\n");
                foreach (var network in block.Networks)
                {
                    doc.Append("## Rete ").Append(PackageWriter.Text(network.Id)).Append(" · ").AppendLine(PackageWriter.Text(network.Language));
                    if (network.Source != null) doc.AppendLine("\nTesto SCL ricostruito dai token; verificare sempre l'XML originale.\n").AppendLine(Code(network.Source, "scl"));
                    if (network.GraphXml != null) doc.AppendLine("\nNetworkSource XML originale (anche collegamenti e polarità delle reti grafiche):\n").AppendLine(Code(network.GraphXml, "xml"));
                    else doc.AppendLine("\nReport compatto: leggere NetworkSource della rete nell'XML originale collegato sopra.\n");
                }
                PackageWriter.WriteText(directory, relative, doc.ToString());
            }
            report.AppendLine("\n## Dipendenze e chiamate\n");
            foreach (var dependency in analysis.Dependencies)
            {
                cancellation.ThrowIfCancellationRequested();
                var target = dependency.TargetId == null ? "u" + ExportPaths.Hash(dependency.FromId + dependency.TargetName) : "b" + dependency.TargetId;
                if (dependency.TargetId == null) graph.Append("  ").Append(target).Append(" [style=dashed,label=\"").Append(Dot(dependency.TargetName)).AppendLine("\"];");
                graph.Append("  b").Append(dependency.FromId).Append(" -> ").Append(target).Append(" [label=\"")
                    .Append(Dot(dependency.Relation + " / " + dependency.Network + " / " + dependency.Evidence)).AppendLine("\"];");
                report.Append("- ").Append(PackageWriter.Text(dependency.FromId)).Append(" → ").Append(PackageWriter.Text(dependency.TargetName))
                    .Append(" · rete ").Append(PackageWriter.Text(dependency.Network)).Append(" · ").AppendLine(PackageWriter.Text(dependency.Resolution + " / " + dependency.Evidence));
            }
            graph.AppendLine("}");
            PackageWriter.WriteText(directory, "dependencies.dot", graph.ToString());
            PackageWriter.WriteText(directory, "logic.md", report.ToString());
            return files.AsReadOnly();
        }
        private static string Dot(string value) { return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", "\\n"); }
        internal static string Code(string content, string language)
        {
            content = content ?? ""; int longest = 0, count = 0;
            foreach (var ch in content) { count = ch == '`' ? count + 1 : 0; longest = Math.Max(longest, count); }
            var fence = new string('`', Math.Max(3, longest + 1));
            return fence + language + "\n" + content + "\n" + fence + "\n";
        }
    }
}
