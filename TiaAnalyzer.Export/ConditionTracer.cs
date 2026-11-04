using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace TiaAnalyzer.Export
{
    public sealed class TraceStep
    {
        public string ArtifactId { get; set; }
        public string Block { get; set; }
        public string File { get; set; }
        public string Network { get; set; }
        public string Plc { get; set; }
        public string Symbol { get; set; }
        public string Description { get; set; }
        public int Depth { get; set; }
        public bool Supported { get; set; }
    }
    public sealed class ConditionTrace
    {
        public string Query { get; set; }
        public List<TraceStep> Steps { get; set; } = new List<TraceStep>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
    public static class ConditionTracer
    {
        private sealed class BudgetExceeded : Exception { }
        public static ConditionTrace Trace(ProjectAnalysis analysis, string query, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Indicare un simbolo o un blocco.");
            var result = new ConditionTrace { Query = query };
            var blocks = analysis.Blocks.ToDictionary(b => b.ArtifactId);
            var writersBySymbol = analysis.Assignments.ToLookup(a => a.TargetKey);
            var callsByTarget = analysis.Calls.ToLookup(c => c.TargetId ?? "");
            var declarationsBySymbol = analysis.Symbols.Where(s => s.Evidence == "Tag declaration").ToLookup(s => SymbolIdentity.Key(s.Name));
            var key = SymbolIdentity.Key(query); var seeds = writersBySymbol[key].ToList();
            var seen = new HashSet<string>();
            Action<AssignmentEvidence, int> follow = null;
            follow = (assignment, depth) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (result.Steps.Count >= 1000) throw new BudgetExceeded();
                if (depth > 16) { result.Warnings.Add("Traccia parziale: limite di profondità raggiunto."); return; }
                var owner = blocks[assignment.ArtifactId];
                var identity = assignment.IsInvocation + "/" + assignment.ArtifactId + "/" + assignment.Network + "/" + assignment.Position;
                if (!seen.Add(identity)) { if (!assignment.IsInvocation) result.Warnings.Add("Dipendenza ciclica o condivisa: " + owner.Name + "/" + assignment.Target + ". Non viene simulato il valore tra scansioni."); return; }
                result.Steps.Add(new TraceStep { ArtifactId = owner.ArtifactId, Block = owner.Name, File = owner.File, Plc = owner.Plc,
                    Network = assignment.Network, Symbol = assignment.Target, Depth = depth, Supported = assignment.Supported,
                    Description = (assignment.IsInvocation ? "Chiamata " + assignment.Target : assignment.Target + " := " + assignment.Value.Text) + " QUANDO " + assignment.Guard.Text + " · riga " + assignment.Line });
                foreach (var caller in callsByTarget[owner.ArtifactId])
                    follow(new AssignmentEvidence { ArtifactId = caller.ArtifactId, Network = caller.Network, Position = caller.Position, Line = caller.Line,
                        Target = caller.Instance, Value = SclLogicAnalyzer.True(), Guard = caller.Guard ?? SclLogicAnalyzer.True(), IsInvocation = true,
                        Supported = analysis.Coverage.Any(c => c.ArtifactId == caller.ArtifactId && c.Network == caller.Network && c.Supported) }, depth + 1);
                foreach (var reference in SclLogicAnalyzer.References(assignment.Value).Concat(SclLogicAnalyzer.References(assignment.Guard)).GroupBy(r => r.Symbol).Select(g => g.First()))
                {
                    var writers = writersBySymbol[reference.Symbol].Where(a =>
                        (reference.Symbol.StartsWith("local/", StringComparison.Ordinal) ? a.ArtifactId == owner.ArtifactId :
                            blocks[a.ArtifactId].Project == owner.Project && blocks[a.ArtifactId].Plc == owner.Plc && blocks[a.ArtifactId].Unit == owner.Unit)).ToList();
                    foreach (var writer in writers) follow(writer, depth + 1);
                    if (writers.Count == 0 && reference.Symbol.StartsWith("local/", StringComparison.Ordinal))
                    {
                        foreach (var call in callsByTarget[owner.ArtifactId])
                            foreach (var parameter in call.Parameters.Where(p => SymbolIdentity.Key("#" + p.Formal) == reference.Symbol && p.Association == ":="))
                            {
                                var caller = blocks[call.ArtifactId];
                                if (result.Steps.Count >= 1000) throw new BudgetExceeded();
                                result.Steps.Add(new TraceStep { ArtifactId = caller.ArtifactId, Block = caller.Name, File = caller.File, Plc = caller.Plc,
                                    Network = call.Network, Symbol = reference.Text, Depth = depth + 1, Supported = parameter.Actual.Kind != "Unknown",
                                    Description = "Parametro " + reference.Text + " associato a " + parameter.Actual.Text + " tramite " + call.Instance + "; chiamata QUANDO " + call.Guard?.Text });
                                foreach (var actual in SclLogicAnalyzer.References(parameter.Actual).Concat(SclLogicAnalyzer.References(call.Guard)))
                                    foreach (var writer in writersBySymbol[actual.Symbol].Where(a =>
                                        (actual.Symbol.StartsWith("local/", StringComparison.Ordinal) ? a.ArtifactId == caller.ArtifactId :
                                            blocks[a.ArtifactId].Project == caller.Project && blocks[a.ArtifactId].Plc == caller.Plc && blocks[a.ArtifactId].Unit == caller.Unit))) follow(writer, depth + 2);
                            }
                    }
                    foreach (var symbol in declarationsBySymbol[reference.Symbol].Where(s => s.Project == owner.Project && s.Plc == owner.Plc))
                    {
                        if (result.Steps.Count >= 1000) throw new BudgetExceeded();
                        result.Steps.Add(new TraceStep { ArtifactId = symbol.ArtifactId, Block = symbol.Block, File = symbol.File, Plc = symbol.Plc,
                            Network = "", Symbol = symbol.Name, Depth = depth + 1, Supported = true, Description = "Dichiarazione tag " + symbol.DataType + " " + symbol.Address + "; valore runtime non disponibile." });
                    }
                }
            };
            try { foreach (var seed in seeds) follow(seed, 0); }
            catch (BudgetExceeded) { result.Warnings.Add("Traccia parziale: limite di 1000 evidenze raggiunto."); }
            if (seeds.Count == 0) result.Warnings.Add("Nessuna assegnazione del simbolo modellata nel sottoinsieme SCL supportato. Non significa che il simbolo non venga scritto.");
            if (analysis.Coverage.Any(c => !c.Supported)) result.Warnings.Add("Sono presenti reti non modellate: possono contenere altre scritture o condizioni. Consultare XML e cross-reference.");
            result.Warnings.Add("Evidenze offline: non vengono calcolati valori, ordine globale di esecuzione, stato di timer o prevalenza dell'ultima scrittura. Più chiamanti/istanze sono alternative da verificare.");
            result.Warnings = result.Warnings.Distinct().ToList(); return result;
        }
        public static string Markdown(ConditionTrace trace)
        {
            var result = new StringBuilder("# Traccia delle condizioni · " + PackageWriter.Text(trace.Query) + "\n\n");
            foreach (var warning in trace.Warnings) result.Append("- ").AppendLine(PackageWriter.Text(warning));
            foreach (var step in trace.Steps)
                result.Append("\n- ").Append(PackageWriter.Text(step.Plc + " / " + step.Block + " / rete " + step.Network)).Append(" · ")
                    .Append(step.Supported ? "SCL modellato" : "PARZIALE").Append(" · ").AppendLine(PackageWriter.Text(step.Description))
                    .Append("  Fonte: ").AppendLine(PackageWriter.Text(step.File));
            return result.ToString();
        }
    }
}
