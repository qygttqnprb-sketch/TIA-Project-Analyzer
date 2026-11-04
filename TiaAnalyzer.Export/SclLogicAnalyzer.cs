using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TiaAnalyzer.Export
{
    // Deliberately bounded subset: simple lvalues, Boolean expressions, IF branches
    // and named call bindings. Unsupported syntax is coverage loss, never TRUE.
    public static class SclLogicAnalyzer
    {
        internal sealed class Token
        {
            public string Text; public int Start, End, Line; public bool Identifier;
            public bool Is(string text) { return string.Equals(Text, text, StringComparison.OrdinalIgnoreCase); }
        }
        internal sealed class Unsupported : Exception { public Unsupported(string reason) : base(reason) { } }
        internal static List<Token> Lex(string text)
        {
            var masked = XmlProjectAnalyzer.MaskCommentsAndStrings(text, false); var result = new List<Token>(); int index = 0, line = 1;
            while (index < masked.Length)
            {
                if (char.IsWhiteSpace(masked[index])) { if (masked[index] == '\n') line++; index++; continue; }
                int start = index, startLine = line; char ch = masked[index]; bool identifier = false;
                if (ch == '\'')
                {
                    index++; bool closed = false;
                    while (index < masked.Length)
                    {
                        if (masked[index] == '$' && index + 1 < masked.Length) { index += 2; continue; }
                        if (masked[index++] != '\'') continue;
                        if (index < masked.Length && masked[index] == '\'') { index++; continue; }
                        closed = true; break;
                    }
                    if (!closed) throw new Unsupported("Stringa SCL non terminata.");
                }
                else if (ch == '#' || ch == '"' || ch == '%' || char.IsLetter(ch) || ch == '_')
                {
                    identifier = true;
                    if (ch == '#' || ch == '%') index++;
                    do
                    {
                        if (index < masked.Length && masked[index] == '"')
                        {
                            index++; bool closed = false;
                            while (index < masked.Length)
                            {
                                if (masked[index++] != '"') continue;
                                if (index < masked.Length && masked[index] == '"') { index++; continue; } closed = true; break;
                            }
                            if (!closed) throw new Unsupported("Identificatore SCL non terminato.");
                        }
                        else while (index < masked.Length && (char.IsLetterOrDigit(masked[index]) || masked[index] == '_')) index++;
                        if (index < masked.Length && masked[index] == '.' && index + 1 < masked.Length && masked[index + 1] != '.') index++;
                        else break;
                    } while (index < masked.Length);
                }
                else if (char.IsDigit(ch))
                { while (index < masked.Length && (char.IsLetterOrDigit(masked[index]) || masked[index] == '_' || masked[index] == '#')) index++; }
                else if (index + 1 < masked.Length && new[] { ":=", "=>", "<=", ">=", "<>" }.Contains(masked.Substring(index, 2))) index += 2;
                else index++;
                if (index == start) index++;
                for (int position = start; position < index; position++) if (masked[position] == '\n') line++;
                result.Add(new Token { Text = masked.Substring(start, index - start), Start = start, End = index, Line = startLine, Identifier = identifier });
                if (result.Count > 100000) throw new Unsupported("Limite di 100000 token per rete superato.");
            }
            return result;
        }
        public static void Parse(ProjectAnalysis analysis, CancellationToken cancellation)
        {
            var parsed = new HashSet<string>(analysis.Coverage.Select(c => c.ArtifactId + "\0" + c.Network));
            foreach (var block in analysis.Blocks)
            {
                if (block.Networks.Count == 0 && new[] { "OB", "FB", "FC" }.Contains(block.Kind))
                    analysis.Coverage.Add(new LogicCoverage { ArtifactId = block.ArtifactId, Network = "", Reason = "Reti non disponibili." });
                foreach (var network in block.Networks)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (network.Source == null && parsed.Contains(block.ArtifactId + "\0" + network.Id)) continue;
                    var coverage = new LogicCoverage { ArtifactId = block.ArtifactId, Network = network.Id, Supported = false };
                    analysis.Coverage.Add(coverage);
                    if (network.Source == null) { coverage.Reason = "Linguaggio grafico/non SCL: leggere l'XML originale."; continue; }
                    var before = analysis.Assignments.Count;
                    try
                    {
                        var tokens = Lex(network.Source);
                        var parser = new Statements(network.Source, tokens, block.ArtifactId, network.Id, analysis, cancellation);
                        parser.Sequence(null, True(), 0);
                        coverage.Supported = !parser.Partial;
                        coverage.Reason = parser.Partial ? "Espressioni o chiamate non completamente risolte." : "Sottoinsieme SCL supportato; nessuna esecuzione runtime.";
                        if (tokens.Any(t => t.Identifier && t.Text.StartsWith("%")))
                        { coverage.Supported = false; coverage.Reason = "Accessi assoluti: sovrapposizioni degli indirizzi non modellate."; }
                    }
                    catch (Unsupported ex) { coverage.Reason = ex.Message; }
                    if (!coverage.Supported)
                        for (int i = before; i < analysis.Assignments.Count; i++) analysis.Assignments[i].Supported = false;
                }
            }
        }
        internal static LogicExpression True() { return new LogicExpression { Kind = "Literal", Text = "TRUE" }; }
        internal static LogicExpression Combine(string kind, LogicExpression left, LogicExpression right = null)
        {
            var children = right == null ? new List<LogicExpression> { left } : new List<LogicExpression> { left, right };
            return new LogicExpression { Kind = kind, Children = children,
                Text = right == null ? "NOT (" + left.Text + ")" : "(" + left.Text + ") " + kind.ToUpperInvariant() + " (" + right.Text + ")" };
        }
        internal static IEnumerable<LogicExpression> References(LogicExpression expression)
        {
            if (expression == null) yield break;
            if (expression.Kind == "Reference") yield return expression;
            foreach (var child in expression.Children ?? new List<LogicExpression>())
                foreach (var item in References(child)) yield return item;
        }
        private static LogicExpression Expression(List<Token> tokens, string source)
        {
            var text = tokens.Count == 0 ? "" : source.Substring(tokens[0].Start, tokens[tokens.Count - 1].End - tokens[0].Start);
            try
            {
                var parser = new Expressions(tokens); var result = parser.Parse(0, 0);
                if (parser.Index != tokens.Count) throw new Unsupported("Espressione non supportata.");
                result.Text = text; return result;
            }
            catch (Unsupported)
            {
                return new LogicExpression { Kind = "Unknown", Text = text,
                    Children = tokens.Where(t => t.Identifier && !new[] { "TRUE", "FALSE", "AND", "OR", "XOR", "NOT" }.Contains(t.Text.ToUpperInvariant()))
                        .Select(t => new LogicExpression { Kind = "Reference", Text = t.Text, Symbol = SymbolIdentity.Key(t.Text) }).ToList() };
            }
        }
        private sealed class Expressions
        {
            private readonly List<Token> tokens; public int Index;
            public Expressions(List<Token> tokens) { this.tokens = tokens; }
            private static int Precedence(string text)
            {
                switch (text.ToUpperInvariant())
                { case "OR": return 1; case "XOR": return 2; case "AND": return 3; case "=": case "<>": case "<": case ">": case "<=": case ">=": return 4; default: return -1; }
            }
            public LogicExpression Parse(int minimum, int depth)
            {
                if (depth > 128 || Index >= tokens.Count) throw new Unsupported("Espressione incompleta o troppo profonda.");
                var token = tokens[Index++]; LogicExpression left;
                if (token.Is("NOT")) left = Combine("Not", Parse(5, depth + 1));
                else if (token.Is("("))
                { left = Parse(0, depth + 1); if (Index >= tokens.Count || !tokens[Index++].Is(")")) throw new Unsupported("Parentesi non bilanciate."); }
                else if (token.Is("TRUE") || token.Is("FALSE") || !token.Identifier)
                {
                    if (!token.Is("TRUE") && !token.Is("FALSE") && !char.IsDigit(token.Text[0]) && token.Text[0] != '\'') throw new Unsupported("Operando non supportato.");
                    left = new LogicExpression { Kind = "Literal", Text = token.Text };
                }
                else left = new LogicExpression { Kind = "Reference", Text = token.Text, Symbol = SymbolIdentity.Key(token.Text) };
                while (Index < tokens.Count && Precedence(tokens[Index].Text) >= minimum)
                {
                    var op = tokens[Index++]; var right = Parse(Precedence(op.Text) + 1, depth + 1);
                    left = Combine(op.Text.ToUpperInvariant() == "AND" ? "And" : op.Text.ToUpperInvariant() == "OR" ? "Or" :
                        op.Text.ToUpperInvariant() == "XOR" ? "Xor" : op.Text, left, right);
                }
                return left;
            }
        }
        private sealed class Statements
        {
            private readonly string source, artifact, network; private readonly List<Token> tokens;
            private readonly ProjectAnalysis analysis; private readonly CancellationToken cancellation;
            private int index; public bool Partial;
            public Statements(string source, List<Token> tokens, string artifact, string network, ProjectAnalysis analysis, CancellationToken cancellation)
            { this.source = source; this.tokens = tokens; this.artifact = artifact; this.network = network; this.analysis = analysis; this.cancellation = cancellation; }
            private bool At(string word) { return index < tokens.Count && tokens[index].Is(word); }
            public void Sequence(string[] stop, LogicExpression guard, int depth)
            {
                if (depth > 64) throw new Unsupported("IF annidati oltre il limite supportato.");
                while (index < tokens.Count && (stop == null || !stop.Any(At)))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (At(";")) { index++; continue; }
                    if (At("IF")) { Branch(guard, depth); continue; }
                    if (new[] { "FOR", "WHILE", "REPEAT", "CASE", "RETURN", "EXIT", "CONTINUE", "GOTO", "REGION", "END_REGION", "ELSIF", "ELSE", "END_IF" }.Any(At))
                        throw new Unsupported("Controllo di flusso non modellato: " + tokens[index].Text);
                    int start = index;
                    if (tokens[index].Identifier && index + 1 < tokens.Count && tokens[index + 1].Is(":="))
                    {
                        var target = tokens[index]; index += 2; var expression = Until(";");
                        var value = Expression(expression, source);
                        var assignment = new AssignmentEvidence { ArtifactId = artifact, Network = network, Line = target.Line,
                            Position = target.Start, Target = target.Text, TargetKey = SymbolIdentity.Key(target.Text), Value = value, Guard = guard,
                            Supported = value.Kind != "Unknown" && !HasUnknown(guard) };
                        analysis.Assignments.Add(assignment); if (!assignment.Supported) Partial = true;
                    }
                    else if (tokens[index].Identifier && index + 1 < tokens.Count && tokens[index + 1].Is("("))
                    {
                        var call = new CallBinding { ArtifactId = artifact, Network = network, Instance = tokens[index].Text, Guard = guard,
                            Position = tokens[index].Start, Line = tokens[index].Line };
                        index += 2; int nested = 0, parameterStart = index;
                        while (index < tokens.Count)
                        {
                            if (At(")") && nested == 0) { AddParameter(call, parameterStart, index); index++; break; }
                            if (At(",") && nested == 0) { AddParameter(call, parameterStart, index); parameterStart = index + 1; }
                            if (At("(")) nested++; else if (At(")")) nested--;
                            index++;
                        }
                        analysis.Calls.Add(call);
                        if (!At(";")) throw new Unsupported("Chiamata incompleta o espressione di chiamata non supportata.");
                    }
                    else throw new Unsupported("Istruzione non supportata vicino a: " + tokens[start].Text);
                    if (!At(";")) throw new Unsupported("Istruzione priva di terminatore."); index++;
                }
            }
            private void Branch(LogicExpression parent, int depth)
            {
                index++; var condition = Expression(Until("THEN"), source); index++;
                if (HasUnknown(condition)) Partial = true;
                var previous = condition;
                Sequence(new[] { "ELSIF", "ELSE", "END_IF" }, Combine("And", parent, condition), depth + 1);
                while (At("ELSIF"))
                {
                    index++; var next = Expression(Until("THEN"), source); index++;
                    if (HasUnknown(next)) Partial = true;
                    Sequence(new[] { "ELSIF", "ELSE", "END_IF" }, Combine("And", parent, Combine("And", Combine("Not", previous), next)), depth + 1);
                    previous = Combine("Or", previous, next);
                }
                if (At("ELSE")) { index++; Sequence(new[] { "END_IF" }, Combine("And", parent, Combine("Not", previous)), depth + 1); }
                if (!At("END_IF")) throw new Unsupported("IF senza END_IF."); index++; if (At(";")) index++;
            }
            private List<Token> Until(string end)
            {
                int start = index;
                while (index < tokens.Count && !At(end)) index++;
                if (index == tokens.Count) throw new Unsupported("Terminatore mancante: " + end);
                return tokens.GetRange(start, index - start);
            }
            private void AddParameter(CallBinding call, int start, int end)
            {
                if (start == end) return;
                if (end - start < 3 || (!tokens[start + 1].Is(":=") && !tokens[start + 1].Is("=>"))) { Partial = true; return; }
                var actual = Expression(tokens.GetRange(start + 2, end - start - 2), source); if (HasUnknown(actual)) Partial = true;
                call.Parameters.Add(new ParameterBinding { Formal = tokens[start].Text, Association = tokens[start + 1].Text, Actual = actual });
            }
        }
        private static bool HasUnknown(LogicExpression expression) { return expression.Kind == "Unknown" || expression.Children.Any(HasUnknown); }
        public static void ResolveCalls(ProjectAnalysis analysis)
        {
            var blocks = analysis.Blocks.ToDictionary(b => b.ArtifactId);
            var byName = analysis.Blocks.ToLookup(b => Tuple.Create(b.Project, b.Plc, b.Unit, b.Name.ToUpperInvariant()));
            var types = analysis.Symbols.Where(s => s.Evidence == "Declaration").ToLookup(s => s.ArtifactId);
            var edges = new HashSet<string>(analysis.Dependencies.Where(d => d.TargetId != null).Select(d => d.FromId + "/" + d.Network + "/" + d.TargetId));
            foreach (var call in analysis.Calls)
            {
                call.TargetId = null; var owner = blocks[call.ArtifactId]; string name = call.Instance.Trim('"');
                if (call.Instance.StartsWith("#"))
                    name = types[owner.ArtifactId].FirstOrDefault(s => SymbolIdentity.Key("#" + s.Name) == SymbolIdentity.Key(call.Instance))?.DataType?.Trim('"');
                var candidates = byName[Tuple.Create(owner.Project, owner.Plc, owner.Unit, (name ?? "").ToUpperInvariant())].Where(b => b.Kind != "UDT" && b.Kind != "TagTable").ToList();
                if (candidates.Count != 1)
                {
                    foreach (var coverage in analysis.Coverage.Where(c => c.ArtifactId == call.ArtifactId && c.Network == call.Network))
                    { coverage.Supported = false; coverage.Reason = "Chiamata non risolta: possibili effetti non modellati."; }
                    continue;
                }
                var target = candidates[0];
                if (target.Kind.IndexOf("DB", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var instance = analysis.Dependencies.Where(d => d.FromId == target.ArtifactId && d.Relation == "InstanceOf" && d.TargetId != null).ToList();
                    if (instance.Count != 1) continue; call.TargetId = instance[0].TargetId;
                }
                else call.TargetId = target.ArtifactId;
                if (call.TargetId != null && edges.Add(call.ArtifactId + "/" + call.Network + "/" + call.TargetId))
                    analysis.Dependencies.Add(new DependencyEvidence { FromId = call.ArtifactId, TargetName = blocks[call.TargetId].Name, TargetId = call.TargetId,
                        Relation = "Call", Network = call.Network, Evidence = "SCL named call binding", Resolution = "Resolved" });
                if (call.TargetId != null && (types[call.TargetId].Any(s => s.Scope == "InOut") || call.Parameters.Any(p => p.Association == "=>")))
                    foreach (var coverage in analysis.Coverage.Where(c => c.ArtifactId == call.ArtifactId && c.Network == call.Network))
                    { coverage.Supported = false; coverage.Reason = "Chiamata con parametri IN_OUT/uscita: alias e scritture indirette non modellati."; }
            }
            var unsupported = new HashSet<string>(analysis.Coverage.Where(c => !c.Supported).Select(c => c.ArtifactId + "/" + c.Network));
            foreach (var assignment in analysis.Assignments)
                if (unsupported.Contains(assignment.ArtifactId + "/" + assignment.Network)) assignment.Supported = false;
        }
    }
}
