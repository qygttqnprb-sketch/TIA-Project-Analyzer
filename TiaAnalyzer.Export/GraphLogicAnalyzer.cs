using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace TiaAnalyzer.Export
{
    // Boolean evidence from FlgNet, never a PLC scan or a general instruction emulator.
    public static class GraphLogicAnalyzer
    {
        private sealed class Unsupported : Exception { public Unsupported(string message) : base(message) { } }
        public static void Parse(ProjectAnalysis analysis, BlockAnalysis block, NetworkEvidence network, XElement source, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var coverage = new LogicCoverage { ArtifactId = block.ArtifactId, Network = network.Id };
            analysis.Coverage.Add(coverage);
            var first = analysis.Assignments.Count;
            try
            {
                var roots = source.Elements().ToList();
                if (roots.Count != 1 || roots[0].Name.LocalName != "FlgNet" ||
                    roots[0].Name.NamespaceName != "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4")
                    throw new Unsupported("Formato FlgNet diverso da v4: leggere XML e cross-reference.");
                if (network.Language != "LAD" && network.Language != "FBD")
                    throw new Unsupported("Linguaggio della rete grafica non riconosciuto.");
                if (roots[0].Descendants().Any(e => e.Name.Namespace != roots[0].Name.Namespace))
                    throw new Unsupported("Namespace misti nella rete FlgNet.");
                var parser = new Network(roots[0], network.Language, cancellation);
                parser.Extract(analysis, block, network);
                coverage.Supported = parser.Problems.Count == 0;
                coverage.Reason = coverage.Supported ? "Sottoinsieme booleano FlgNet v4; stato e ordine di esecuzione non simulati." :
                    string.Join(" ", parser.Problems.Distinct().Take(12));
            }
            catch (Unsupported ex) { coverage.Reason = ex.Message; }
            if (!coverage.Supported)
                for (int i = first; i < analysis.Assignments.Count; i++) analysis.Assignments[i].Supported = false;
        }
        private sealed class Net
        {
            public string Id;
            public List<XElement> Ends;
        }
        private sealed class Network
        {
            private readonly Dictionary<string, XElement> elements = new Dictionary<string, XElement>(StringComparer.Ordinal);
            private readonly Dictionary<string, Net> pins = new Dictionary<string, Net>(StringComparer.Ordinal);
            private readonly List<Net> nets = new List<Net>();
            private readonly Dictionary<string, LogicExpression> memo = new Dictionary<string, LogicExpression>();
            private readonly HashSet<string> active = new HashSet<string>();
            private readonly CancellationToken cancellation;
            private readonly string language;
            private int steps, position;
            public readonly List<string> Problems = new List<string>();
            private static readonly HashSet<string> BooleanParts = new HashSet<string> { "Contact", "Coil", "SCoil", "RCoil", "A", "O" };
            public Network(XElement root, string language, CancellationToken cancellation)
            {
                this.language = language; this.cancellation = cancellation;
                if (root.Elements().Count() != 2 || root.Elements().Count(e => e.Name.LocalName == "Parts") != 1 ||
                    root.Elements().Count(e => e.Name.LocalName == "Wires") != 1)
                    throw new Unsupported("Struttura Parts/Wires non riconosciuta.");
                var parts = root.Elements().Single(e => e.Name.LocalName == "Parts").Elements().ToList();
                var wires = root.Elements().Single(e => e.Name.LocalName == "Wires").Elements().ToList();
                if (parts.Count > 2048 || wires.Count > 8192) throw new Unsupported("Limite grafico: 2048 oggetti o 8192 collegamenti per rete.");
                var ids = new HashSet<string>();
                foreach (var element in parts)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var id = (string)element.Attribute("UId");
                    if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new Unsupported("UId mancante o duplicato nella rete.");
                    elements.Add(id, element);
                    if (element.Name.LocalName != "Access" && (element.Name.LocalName != "Part" || !BooleanParts.Contains(Name(element))))
                        Problems.Add("Istruzione non modellata: " + (Name(element) ?? element.Name.LocalName) + ".");
                    if (element.Name.LocalName == "Part" && BooleanParts.Contains(Name(element))) ValidatePart(element);
                }
                foreach (var wire in wires)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var id = (string)wire.Attribute("UId");
                    if (wire.Name.LocalName != "Wire" || string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new Unsupported("Collegamento o UId non valido.");
                    var net = new Net { Id = id, Ends = wire.Elements().ToList() };
                    if (net.Ends.Count < 2 || net.Ends.Count > 2048) throw new Unsupported("Numero di terminali del collegamento non supportato.");
                    foreach (var end in net.Ends)
                    {
                        if (end.Name.LocalName == "NameCon")
                        {
                            XElement part; var pin = (string)end.Attribute("Name"); var uid = (string)end.Attribute("UId");
                            if (uid == null || !elements.TryGetValue(uid, out part) || part.Name.LocalName == "Access" || string.IsNullOrEmpty(pin))
                                throw new Unsupported("Pin riferito a un oggetto inesistente.");
                            var key = uid + "/" + pin;
                            if (pins.ContainsKey(key)) throw new Unsupported("Pin collegato più volte: " + key + ".");
                            pins.Add(key, net);
                            if (part.Name.LocalName == "Part" && BooleanParts.Contains(Name(part)) && !AllowedPins(part).Contains(pin))
                                Problems.Add("Pin non modellato: " + key + ".");
                        }
                        else if (end.Name.LocalName == "IdentCon")
                        {
                            XElement access; var uid = (string)end.Attribute("UId");
                            if (uid == null || !elements.TryGetValue(uid, out access) || access.Name.LocalName != "Access")
                                throw new Unsupported("Operando riferito a un accesso inesistente.");
                        }
                        else if (end.Name.LocalName != "Powerrail" && end.Name.LocalName != "OpenCon")
                            throw new Unsupported("Tipo di collegamento non modellato: " + end.Name.LocalName + ".");
                    }
                    nets.Add(net);
                }
            }
            private static string Name(XElement element) { return (string)element.Attribute("Name"); }
            private static string Uid(XElement element) { return (string)element.Attribute("UId"); }
            private int Cardinality(XElement part)
            {
                var cards = part.Elements().Where(e => e.Name.LocalName == "TemplateValue" && Name(e) == "Card").ToList(); int count;
                if (cards.Count != 1 || !int.TryParse(cards[0].Value, out count) || count < 2 || count > 64)
                    throw new Unsupported("Cardinalità A/O non supportata.");
                return count;
            }
            private IEnumerable<string> AllowedPins(XElement part)
            {
                if (Name(part) == "A" || Name(part) == "O") return Enumerable.Range(1, Cardinality(part)).Select(i => "in" + i).Concat(new[] { "out" });
                return new[] { "in", "out", "operand" };
            }
            private void ValidatePart(XElement part)
            {
                if (Name(part) == "Contact" && language != "LAD") Problems.Add("Contact fuori dal sottoinsieme LAD.");
                var allowed = AllowedPins(part).ToList();
                foreach (var attribute in part.Attributes().Where(a => a.Name.LocalName != "Name" && a.Name.LocalName != "UId"))
                    Problems.Add("Attributo di istruzione non modellato: " + attribute.Name.LocalName + ".");
                foreach (var child in part.Elements())
                    if (child.Name.LocalName == "Negated")
                    {
                        if (!allowed.Contains(Name(child)) || (Name(part) != "A" && Name(part) != "O" && Name(child) != "operand"))
                            Problems.Add("Negazione su pin non supportato.");
                    }
                    else if (child.Name.LocalName != "TemplateValue" || (Name(part) != "A" && Name(part) != "O") || Name(child) != "Card")
                        Problems.Add("Proprietà di istruzione non modellata: " + child.Name.LocalName + ".");
                if (part.Elements().Where(e => e.Name.LocalName == "Negated").GroupBy(Name).Any(g => g.Count() > 1))
                    Problems.Add("Negazione duplicata.");
            }
            private void Tick(int depth)
            {
                cancellation.ThrowIfCancellationRequested();
                if (depth > 64 || ++steps > 20000) throw new Unsupported("Limite di profondità o complessità della rete raggiunto.");
            }
            private static LogicExpression Unknown(string reason) { return new LogicExpression { Kind = "Unknown", Text = "NON MODELLATO: " + reason }; }
            private static LogicExpression Combine(string kind, LogicExpression left, LogicExpression right = null)
            {
                if (left.Text.Length + (right?.Text.Length ?? 0) > 8192) throw new Unsupported("Espressione grafica troppo estesa.");
                return SclLogicAnalyzer.Combine(kind, left, right);
            }
            private static LogicExpression Literal(bool value) { return new LogicExpression { Kind = "Literal", Text = value ? "TRUE" : "FALSE" }; }
            private static LogicExpression Access(XElement access, bool target)
            {
                var scope = (string)access.Attribute("Scope");
                if (!target && scope == "LiteralConstant")
                {
                    var constants = access.Elements().ToList();
                    if (constants.Count != 1 || constants[0].Name.LocalName != "Constant")
                        throw new Unsupported("Costante non riconosciuta.");
                    var constant = constants[0];
                    var type = constant?.Elements().FirstOrDefault(e => e.Name.LocalName == "ConstantType")?.Value;
                    var value = constant?.Elements().FirstOrDefault(e => e.Name.LocalName == "ConstantValue")?.Value;
                    bool parsed;
                    if (string.Equals(type, "Bool", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out parsed)) return Literal(parsed);
                    throw new Unsupported("Costante non booleana o non riconosciuta.");
                }
                if (scope != "LocalVariable" && scope != "GlobalVariable") throw new Unsupported("Scope operando non modellato: " + scope + ".");
                var symbols = access.Elements().Where(e => e.Name.LocalName == "Symbol").ToList();
                if (symbols.Count != 1 || access.Elements().Count() != 1) throw new Unsupported("Operando composto non supportato.");
                var components = symbols[0].Elements().ToList();
                if (components.Count == 0 || components.Any(c => c.Name.LocalName != "Component" || string.IsNullOrEmpty(Name(c)) || c.HasElements))
                    throw new Unsupported("Operando indicizzato o indiretto non modellato.");
                var text = (scope == "LocalVariable" ? "#" : "") + string.Join(".", components.Select(c => "\"" + Name(c).Replace("\"", "\"\"") + "\""));
                return new LogicExpression { Kind = "Reference", Text = text, Symbol = SymbolIdentity.Key(text) };
            }
            private Net Pin(XElement part, string pin)
            {
                Net net;
                if (!pins.TryGetValue(Uid(part) + "/" + pin, out net)) throw new Unsupported("Pin non collegato: " + Uid(part) + "/" + pin + ".");
                return net;
            }
            private bool IsOutput(XElement end)
            {
                if (end.Name.LocalName != "NameCon") return false;
                var part = elements[Uid(end)];
                if (part.Name.LocalName == "Part" && BooleanParts.Contains(Name(part))) return Name(end) == "out";
                return part.Name.LocalName == "Call" && part.Descendants().Any(p => p.Name.LocalName == "Parameter" && Name(p) == Name(end) &&
                    ((string)p.Attribute("Section") == "Output" || (string)p.Attribute("Section") == "InOut"));
            }
            private LogicExpression Signal(Net net, int depth)
            {
                Tick(depth); LogicExpression cached;
                if (memo.TryGetValue(net.Id, out cached)) return cached;
                if (!active.Add(net.Id)) throw new Unsupported("Ciclo nel grafo: stato non simulato.");
                try
                {
                    var drivers = net.Ends.Where(e => e.Name.LocalName == "Powerrail" || IsOutput(e)).ToList();
                    if (drivers.Any(e => e.Name.LocalName == "Powerrail") && (language != "LAD" || net.Ends.Any(e => e.Name.LocalName == "IdentCon")))
                        throw new Unsupported("Powerrail non compatibile con il collegamento.");
                    if (net.Ends.Any(e => e.Name.LocalName == "OpenCon") ||
                        net.Ends.Any(e => e.Name.LocalName == "NameCon" && !IsOutput(e) && !BooleanParts.Contains(Name(elements[Uid(e)]))))
                        throw new Unsupported("Collegamento aperto o istruzione non modellata.");
                    LogicExpression result = null;
                    if (drivers.Count == 0)
                    {
                        var accesses = net.Ends.Where(e => e.Name.LocalName == "IdentCon").ToList();
                        if (accesses.Count != 1) throw new Unsupported("Nessuna sorgente univoca del segnale.");
                        result = Access(elements[Uid(accesses[0])], false);
                    }
                    else
                    {
                        if (drivers.Count > 1 && (language != "LAD" || drivers.Any(e => e.Name.LocalName != "NameCon" || Name(elements[Uid(e)]) != "Contact")))
                            throw new Unsupported("Più sorgenti sul collegamento non modellate.");
                        foreach (var driver in drivers)
                        {
                            var value = driver.Name.LocalName == "Powerrail" ? Literal(true) : Output(elements[Uid(driver)], depth + 1);
                            result = result == null ? value : Combine("Or", result, value);
                        }
                    }
                    memo.Add(net.Id, result); return result;
                }
                finally { active.Remove(net.Id); }
            }
            private LogicExpression Input(XElement part, string pin, int depth)
            {
                var value = Signal(Pin(part, pin), depth + 1);
                return part.Elements().Any(e => e.Name.LocalName == "Negated" && Name(e) == pin) ? Combine("Not", value) : value;
            }
            private LogicExpression Output(XElement part, int depth)
            {
                Tick(depth);
                if (part.Name.LocalName != "Part" || !BooleanParts.Contains(Name(part))) throw new Unsupported("Uscita di chiamata non modellata.");
                LogicExpression value;
                if (Name(part) == "Contact") value = Combine("And", Input(part, "in", depth), Operand(part, false));
                else if (Name(part) == "A" || Name(part) == "O")
                {
                    value = Input(part, "in1", depth);
                    for (int i = 2; i <= Cardinality(part); i++) value = Combine(Name(part) == "A" ? "And" : "Or", value, Input(part, "in" + i, depth));
                    if (part.Elements().Any(e => e.Name.LocalName == "Negated" && Name(e) == "out")) value = Combine("Not", value);
                }
                else value = Input(part, "in", depth);
                return value;
            }
            private LogicExpression Operand(XElement part, bool target)
            {
                var net = Pin(part, "operand"); var accesses = net.Ends.Where(e => e.Name.LocalName == "IdentCon").ToList();
                if (accesses.Count != 1 || net.Ends.Count != 2) throw new Unsupported("Operando non collegato in modo univoco.");
                var value = Access(elements[Uid(accesses[0])], target);
                if (!target && part.Elements().Any(e => e.Name.LocalName == "Negated" && Name(e) == "operand")) value = Combine("Not", value);
                return value;
            }
            private LogicExpression Try(Func<LogicExpression> read)
            {
                try { return read(); }
                catch (Unsupported ex) { Problems.Add(ex.Message); return Unknown(ex.Message); }
            }
            private void Add(ProjectAnalysis analysis, BlockAnalysis block, NetworkEvidence network, LogicExpression target,
                LogicExpression value, LogicExpression guard, string operation, string location)
            {
                analysis.Assignments.Add(new AssignmentEvidence { ArtifactId = block.ArtifactId, Network = network.Id, Language = language,
                    Position = ++position, Location = location, Target = target.Text, TargetKey = target.Symbol,
                    Value = value, Guard = guard, Operation = operation, Supported = value.Kind != "Unknown" && guard.Kind != "Unknown" });
            }
            public void Extract(ProjectAnalysis analysis, BlockAnalysis block, NetworkEvidence network)
            {
                foreach (var part in elements.Values.Where(e => e.Name.LocalName == "Part" && BooleanParts.Contains(Name(e))))
                {
                    cancellation.ThrowIfCancellationRequested();
                    Try(() => Output(part, 0));
                    if (Name(part) == "Contact") continue;
                    if (Name(part) == "A" || Name(part) == "O") continue;
                    var target = Try(() => Operand(part, true));
                    if (target.Kind != "Reference") continue;
                    var signal = Try(() => Input(part, "in", 0));
                    var operation = Name(part) == "SCoil" ? "Set" : Name(part) == "RCoil" ? "Reset" : "Assign";
                    var value = operation == "Assign" ? signal : Literal(operation == "Set");
                    if (operation == "Assign" && part.Elements().Any(e => e.Name.LocalName == "Negated" && Name(e) == "operand")) value = Combine("Not", value);
                    if (operation != "Assign" && part.Elements().Any(e => e.Name.LocalName == "Negated")) Problems.Add("Negazione su bobina Set/Reset non modellata.");
                    Add(analysis, block, network, target, value, operation == "Assign" ? Literal(true) : signal, operation, "UId " + Uid(part) + " / operand");
                }
                foreach (var net in nets.Where(n => n.Ends.Any(IsOutput) && n.Ends.Any(e => e.Name.LocalName == "IdentCon")))
                {
                    foreach (var end in net.Ends.Where(e => e.Name.LocalName == "IdentCon"))
                    {
                        var target = Try(() => Access(elements[Uid(end)], true));
                        if (target.Kind == "Reference") Add(analysis, block, network, target, Try(() => Signal(net, 0)), Literal(true),
                            net.Ends.Any(e => e.Name.LocalName == "NameCon" && elements[Uid(e)].Name.LocalName == "Call") ? "CallOutput" : "Assign", "Wire UId " + net.Id);
                    }
                }
                foreach (var net in nets)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (net.Ends.Any(e => e.Name.LocalName == "NameCon" && Name(e) == "operand" &&
                        elements[Uid(e)].Name.LocalName == "Part" && BooleanParts.Contains(Name(elements[Uid(e)])))) continue;
                    if (net.Ends.Count == 2 && net.Ends.Count(IsOutput) == 1 && net.Ends.Count(e => e.Name.LocalName == "OpenCon") == 1) continue;
                    Try(() => Signal(net, 0));
                }
            }
        }
    }
}
