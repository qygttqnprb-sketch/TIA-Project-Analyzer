using System.Collections.Generic;

namespace TiaAnalyzer.Export
{
    public sealed class ProjectAnalysis
    {
        internal readonly HashSet<string> SymbolKeys = new HashSet<string>();
        internal readonly HashSet<string> DependencyKeys = new HashSet<string>();
        public string SchemaVersion { get; set; } = "1.0";
        public List<BlockAnalysis> Blocks { get; set; } = new List<BlockAnalysis>();
        public List<SymbolEvidence> Symbols { get; set; } = new List<SymbolEvidence>();
        public List<DependencyEvidence> Dependencies { get; set; } = new List<DependencyEvidence>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
    public sealed class BlockAnalysis
    {
        public string ArtifactId { get; set; }
        public string Name { get; set; }
        public string File { get; set; }
        public string Project { get; set; }
        public string Plc { get; set; }
        public string Unit { get; set; }
        public string Kind { get; set; }
        public List<NetworkEvidence> Networks { get; set; } = new List<NetworkEvidence>();
    }
    public sealed class NetworkEvidence
    {
        public string Id { get; set; }
        public string Language { get; set; }
        public string Source { get; set; }
        public string GraphXml { get; set; }
    }
    public sealed class SymbolEvidence
    {
        public string ArtifactId { get; set; }
        public string Block { get; set; }
        public string File { get; set; }
        public string Project { get; set; }
        public string Plc { get; set; }
        public string Network { get; set; }
        public string Name { get; set; }
        public string Scope { get; set; }
        public string Evidence { get; set; }
        public string DataType { get; set; }
        public string Address { get; set; }
        public string Display { get { return Name + " · " + Block + " · " + Plc + " · rete " + Network + " · " + Evidence; } }
    }
    public sealed class DependencyEvidence
    {
        public string FromId { get; set; }
        public string TargetName { get; set; }
        public string TargetId { get; set; }
        public string Relation { get; set; }
        public string Network { get; set; }
        public string Evidence { get; set; }
        public string Resolution { get; set; }
    }
}
