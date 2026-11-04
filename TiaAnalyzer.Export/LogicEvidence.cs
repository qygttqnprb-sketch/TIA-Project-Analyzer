using System.Collections.Generic;

namespace TiaAnalyzer.Export
{
    public sealed class LogicExpression
    {
        public string Kind { get; set; }
        public string Text { get; set; }
        public string Symbol { get; set; }
        public List<LogicExpression> Children { get; set; } = new List<LogicExpression>();
    }
    public sealed class AssignmentEvidence
    {
        public string Language { get; set; } = "SCL";
        public string Operation { get; set; } = "Assign";
        public string Location { get; set; }
        public string ArtifactId { get; set; }
        public string Network { get; set; }
        public int Line { get; set; }
        public int Position { get; set; }
        public string Target { get; set; }
        public string TargetKey { get; set; }
        public LogicExpression Value { get; set; }
        public LogicExpression Guard { get; set; }
        public bool Supported { get; set; }
        public bool IsInvocation { get; set; }
    }
    public sealed class ParameterBinding
    {
        public string Formal { get; set; }
        public string Association { get; set; }
        public LogicExpression Actual { get; set; }
    }
    public sealed class CallBinding
    {
        public string ArtifactId { get; set; }
        public string Network { get; set; }
        public string Instance { get; set; }
        public int Position { get; set; }
        public int Line { get; set; }
        public string TargetId { get; set; }
        public LogicExpression Guard { get; set; }
        public List<ParameterBinding> Parameters { get; set; } = new List<ParameterBinding>();
    }
    public sealed class LogicCoverage
    {
        public string ArtifactId { get; set; }
        public string Network { get; set; }
        public bool Supported { get; set; }
        public string Reason { get; set; }
    }
    public static class SymbolIdentity
    {
        // Component boundaries survive quotes: "A.B" is distinct from "A".B.
        public static string Key(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim(); bool local = value.StartsWith("#"); if (local) value = value.Substring(1);
            var parts = new List<string>(); var current = new System.Text.StringBuilder(); bool quoted = false;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '"')
                {
                    if (quoted && i + 1 < value.Length && value[i + 1] == '"') { current.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (ch == '.' && !quoted) { parts.Add(System.Uri.EscapeDataString(current.ToString().ToUpperInvariant())); current.Clear(); }
                else current.Append(ch);
            }
            parts.Add(System.Uri.EscapeDataString(current.ToString().ToUpperInvariant()));
            return (local ? "local/" : "global/") + string.Join("/", parts);
        }
        public static string Name(string key)
        {
            var split = key.IndexOf('/'); return split < 0 ? key : System.Uri.UnescapeDataString(key.Substring(split + 1));
        }
    }
}
