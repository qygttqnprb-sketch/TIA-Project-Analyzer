using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    public sealed class FocusPlan
    {
        public string Query { get; set; }
        public int TotalArtifacts { get; set; }
        public List<string> IncludedIds { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
    public sealed class FocusSelection
    {
        public FocusPlan Plan { get; set; }
        public ExportSnapshot Snapshot { get; set; }
        public ProjectAnalysis Analysis { get; set; }
        public ConditionTrace Trace { get; set; }
    }
    public static class FocusPackage
    {
        public static FocusSelection Select(ExportSnapshot snapshot, ProjectAnalysis analysis, string query, CancellationToken cancellation)
        {
            var trace = ConditionTracer.Trace(analysis, query, cancellation); var key = SymbolIdentity.Key(query);
            var core = new HashSet<string>(analysis.Blocks.Where(b => string.Equals(b.Name, query.Trim(), StringComparison.OrdinalIgnoreCase)).Select(b => b.ArtifactId));
            foreach (var symbol in analysis.Symbols.Where(s => SymbolIdentity.Key(s.Name) == key)) core.Add(symbol.ArtifactId);
            foreach (var assignment in analysis.Assignments.Where(a => a.TargetKey == key)) core.Add(assignment.ArtifactId);
            foreach (var step in trace.Steps) core.Add(step.ArtifactId);
            if (core.Count == 0) throw new InvalidOperationException("Simbolo/blocco non trovato. Cercarlo prima nell'indice della raccolta.");
            var plan = new FocusPlan { Query = query, TotalArtifacts = snapshot.Artifacts.Count };
            var scopes = new HashSet<string>(snapshot.Artifacts.Where(a => core.Contains(a.Id)).Select(a => a.Project + "\0" + a.Plc));
            // Unsupported networks and unresolved calls can hide additional writes.
            var uncertain = new HashSet<string>(analysis.Coverage.Where(c => !c.Supported).Select(c => c.ArtifactId));
            var covered = new HashSet<string>(analysis.Coverage.Select(c => c.ArtifactId));
            foreach (var dependency in analysis.Dependencies.Where(d => d.Relation == "Call" && d.TargetId == null)) uncertain.Add(dependency.FromId);
            if (snapshot.Artifacts.Where(a => scopes.Contains(a.Project + "\0" + a.Plc)).Any(a => a.Status != "Exported" || uncertain.Contains(a.Id) ||
                (new[] { "OB", "FB", "FC" }.Contains(a.Kind) && (!covered.Contains(a.Id) || (a.CrossReferencesStatus != "Available" && a.CrossReferencesStatus != "Demo")))))
            {
                foreach (var artifact in snapshot.Artifacts.Where(a => scopes.Contains(a.Project + "\0" + a.Plc))) core.Add(artifact.Id);
                plan.Warnings.Add("Selezione conservativa: inclusi gli oggetti degli scope PLC interessati perché reti, riferimenti o copertura incompleti possono nascondere altre scritture.");
            }
            var selected = new HashSet<string>(core);
            var downstream = new HashSet<string>(core); bool changed;
            do
            {
                changed = false; cancellation.ThrowIfCancellationRequested();
                foreach (var dependency in analysis.Dependencies.Where(d => d.TargetId != null))
                {
                    if (downstream.Contains(dependency.FromId) && downstream.Add(dependency.TargetId)) { selected.Add(dependency.TargetId); changed = true; }
                    if (selected.Contains(dependency.TargetId) && selected.Add(dependency.FromId)) changed = true;
                    if (selected.Contains(dependency.FromId) && dependency.Relation != "Call" && selected.Add(dependency.TargetId)) changed = true;
                }
            } while (changed);
            foreach (var artifact in snapshot.Artifacts.Where(a => a.Kind == "TagTable" && scopes.Contains(a.Project + "\0" + a.Plc))) selected.Add(artifact.Id);
            plan.IncludedIds = selected.OrderBy(s => s, StringComparer.Ordinal).ToList();
            plan.Warnings.Add("Pacchetto mirato da una raccolta completa; gli oggetti esclusi sono elencati nell'inventario. Le reti dei chiamanti possono menzionare altri blocchi esclusi.");
            var subset = new ExportSnapshot { CreatedUtc = snapshot.CreatedUtc, IsDemo = snapshot.IsDemo, SchemaVersion = snapshot.SchemaVersion,
                AcquisitionState = snapshot.AcquisitionState, ExportMode = snapshot.ExportMode, ExportId = snapshot.ExportId, Inventory = snapshot.Inventory,
                Artifacts = snapshot.Artifacts.Where(a => selected.Contains(a.Id)).ToList(), Components = snapshot.Components, Issues = snapshot.Issues,
                CrossReferences = (snapshot.CrossReferences ?? new List<PlcCrossReference>()).Where(r => selected.Contains(r.QueriedArtifactId)).ToList(),
                Limitations = snapshot.Limitations.Concat(plan.Warnings).ToList(), Notes = snapshot.Notes, Metrics = snapshot.Metrics };
            subset.Selection = new PackageSelection { Query = query, Mode = "Conservative", TotalArtifacts = snapshot.Artifacts.Count,
                IncludedArtifacts = subset.Artifacts.Count, ExcludedIds = snapshot.Artifacts.Where(a => !selected.Contains(a.Id)).Select(a => a.Id).ToList() };
            var result = new ProjectAnalysis { Blocks = analysis.Blocks.Where(b => selected.Contains(b.ArtifactId)).ToList(),
                Symbols = analysis.Symbols.Where(s => selected.Contains(s.ArtifactId)).ToList(), Assignments = analysis.Assignments.Where(a => selected.Contains(a.ArtifactId)).ToList(),
                Calls = analysis.Calls.Where(c => selected.Contains(c.ArtifactId)).ToList(), Coverage = analysis.Coverage.Where(c => selected.Contains(c.ArtifactId)).ToList(),
                CrossReferences = subset.CrossReferences, Warnings = analysis.Warnings.Concat(plan.Warnings).ToList(),
                Dependencies = analysis.Dependencies.Where(d => selected.Contains(d.FromId)).Select(d => new DependencyEvidence { FromId = d.FromId,
                    TargetName = d.TargetName, TargetId = d.TargetId != null && selected.Contains(d.TargetId) ? d.TargetId : null, Relation = d.Relation,
                    Network = d.Network, Evidence = d.Evidence, Resolution = d.TargetId != null && !selected.Contains(d.TargetId) ? "OutsideSelectedPackage" : d.Resolution }).ToList() };
            result.Writes = WriteAnalyzer.Analyze(result, cancellation);
            return new FocusSelection { Plan = plan, Snapshot = subset, Analysis = result, Trace = trace };
        }
    }
}
