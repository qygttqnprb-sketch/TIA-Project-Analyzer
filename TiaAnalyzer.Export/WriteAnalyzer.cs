using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    public sealed class WriteSite
    {
        public string ArtifactId { get; set; }
        public string Block { get; set; }
        public string File { get; set; }
        public string Network { get; set; }
        public string Location { get; set; }
        public string Language { get; set; }
        public string Operation { get; set; }
        public string Value { get; set; }
        public string Guard { get; set; }
        public bool Supported { get; set; }
    }
    public sealed class WriteGroup
    {
        public string Target { get; set; }
        public string TargetKey { get; set; }
        public string ScopeKey { get; set; }
        public string Project { get; set; }
        public string Plc { get; set; }
        public string Unit { get; set; }
        public string LocalBlockId { get; set; }
        public string Classification { get; set; }
        public int DistinctBlocks { get; set; }
        public List<WriteSite> Sites { get; set; } = new List<WriteSite>();
    }
    public sealed class WriteReport
    {
        public List<WriteGroup> Groups { get; set; } = new List<WriteGroup>();
        // Raw access evidence cannot be reliably joined to XML positions or local instance storage.
        public List<PlcCrossReference> NativeWrites { get; set; } = new List<PlcCrossReference>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
    public static class WriteAnalyzer
    {
        public static WriteReport Analyze(ProjectAnalysis analysis, CancellationToken cancellation)
        {
            var result = new WriteReport();
            var blocks = analysis.Blocks.ToDictionary(b => b.ArtifactId);
            var groups = new Dictionary<string, WriteGroup>();
            foreach (var assignment in analysis.Assignments.Where(a => !a.IsInvocation && !string.IsNullOrEmpty(a.TargetKey)))
            {
                cancellation.ThrowIfCancellationRequested();
                var block = blocks[assignment.ArtifactId];
                bool local = assignment.TargetKey.StartsWith("local/", StringComparison.Ordinal);
                var scope = ExportPaths.Identity(block.Project, block.Plc, block.Unit, local ? block.ArtifactId : "", assignment.TargetKey);
                WriteGroup group;
                if (!groups.TryGetValue(scope, out group))
                {
                    group = new WriteGroup { Target = assignment.Target, TargetKey = assignment.TargetKey, ScopeKey = scope,
                        Project = block.Project, Plc = block.Plc, Unit = block.Unit, LocalBlockId = local ? block.ArtifactId : null };
                    groups.Add(scope, group);
                }
                group.Sites.Add(new WriteSite { ArtifactId = block.ArtifactId, Block = block.Name, File = block.File, Network = assignment.Network,
                    Location = assignment.Location ?? "riga SCL ricostruita " + assignment.Line, Language = assignment.Language ?? "SCL",
                    Operation = assignment.Operation ?? "Assign", Value = assignment.Value?.Text, Guard = assignment.Guard?.Text, Supported = assignment.Supported });
            }
            foreach (var group in groups.Values)
            {
                group.DistinctBlocks = group.Sites.Select(s => s.ArtifactId).Distinct().Count();
                group.Classification = group.DistinctBlocks > 1 ? "MultipleBlocks" : group.Sites.Count > 1 ? "MultipleSites" : "SingleSite";
                result.Groups.Add(group);
            }
            result.Groups = result.Groups.OrderByDescending(g => g.DistinctBlocks).ThenByDescending(g => g.Sites.Count)
                .ThenBy(g => g.ScopeKey, StringComparer.Ordinal).ToList();
            var seen = new HashSet<string>();
            foreach (var reference in analysis.CrossReferences ?? new List<PlcCrossReference>())
            {
                cancellation.ThrowIfCancellationRequested();
                var access = (reference.Access ?? "").Replace(" ", "");
                if (!new[] { "Write", "ReadWrite", "Read,Write", "Read|Write" }.Contains(access, StringComparer.OrdinalIgnoreCase)) continue;
                var identity = ExportPaths.Identity(reference.Project, reference.Plc, reference.SourceName, reference.SourceAddress, reference.SourcePath,
                    reference.ReferenceName, reference.ReferencePath, reference.ReferenceDevice, reference.ReferenceAddress, reference.Location, reference.Access, reference.Provider);
                if (seen.Add(identity)) result.NativeWrites.Add(reference);
            }
            if (analysis.Coverage.Any(c => !c.Supported))
                result.Warnings.Add("Copertura parziale: reti non modellate possono contenere ulteriori scritture. L'elenco non dimostra l'assenza di altri siti.");
            result.Warnings.Add("Più siti non dimostrano un conflitto: rami esclusivi, chiamate e istanze possono avere effetti diversi. Non viene stabilita l'ultima scrittura.");
            result.Warnings.Add("Le variabili locali sono separate per blocco, i simboli globali per progetto/PLC/unità. Alias, sovrapposizioni di indirizzo e associazioni IN_OUT/uscita non sono risolti.");
            result.Warnings.Add("I riferimenti di scrittura nativi sono separati dai siti XML: non si sommano ai conteggi e non vengono associati automaticamente a reti o istanze.");
            return result;
        }
        public static WriteReport Select(WriteReport report, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return report;
            query = query.Trim();
            return new WriteReport { Warnings = report.Warnings,
                Groups = report.Groups.Where(g => (g.Target ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    g.TargetKey == SymbolIdentity.Key(query)).ToList(),
                NativeWrites = report.NativeWrites.Where(r => new[] { r.SourceName, r.SourceAddress, r.SourcePath }.Any(s =>
                    (s ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList() };
        }
        public static string Markdown(WriteReport report, int maximumSites = int.MaxValue)
        {
            var result = new StringBuilder("# Siti di scrittura PLC\n\n");
            foreach (var warning in report.Warnings) result.Append("- ").AppendLine(PackageWriter.Text(warning));
            int shown = 0;
            foreach (var group in report.Groups)
            {
                if (shown >= maximumSites) break;
                result.Append("\n## ").AppendLine(PackageWriter.Text(group.Project + " / " + group.Plc + " / " + group.Unit + " / " + group.Target));
                result.Append(group.Sites.Count).Append(" siti in ").Append(group.DistinctBlocks).Append(" blocchi · ")
                    .AppendLine(PackageWriter.Text(group.Classification));
                foreach (var site in group.Sites)
                {
                    if (shown++ >= maximumSites) break;
                    result.Append("\n- ").Append(PackageWriter.Text(site.Block + " / rete " + site.Network + " / " + site.Location + " / " + site.Language + " / " + site.Operation))
                        .AppendLine(site.Supported ? " · modellato" : " · PARZIALE");
                    result.Append("  Valore: ").AppendLine(PackageWriter.Text(site.Value)).Append("  Quando: ").AppendLine(PackageWriter.Text(site.Guard))
                        .Append("  Fonte XML: ").AppendLine(PackageWriter.Text(site.File));
                }
            }
            if (report.Groups.Sum(g => (long)g.Sites.Count) > maximumSites) result.AppendLine("\nAnteprima limitata; elenco completo in writers.json e writers.md del pacchetto.");
            result.AppendLine("\n## Riferimenti di scrittura nativi (separati)\n");
            foreach (var reference in report.NativeWrites.Take(maximumSites))
                result.Append("- ").AppendLine(PackageWriter.Text(reference.Provider + " / " + reference.Plc + " / " + reference.SourceName + " / " +
                    reference.ReferencePath + " / " + reference.Location + " / " + reference.Access));
            if (report.NativeWrites.Count > maximumSites) result.AppendLine("\nAltri riferimenti in writers.json e crossreferences.json.");
            if (report.Groups.Count == 0 && report.NativeWrites.Count == 0) result.AppendLine("\nNessun sito individuato nella copertura disponibile.");
            return result.ToString();
        }
    }
}
