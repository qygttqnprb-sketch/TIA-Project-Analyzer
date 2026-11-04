using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    // Evidence extraction, not execution or a complete Siemens language compiler.
    public static class XmlProjectAnalyzer
    {
        private const long MaxXmlCharacters = 64L * 1024 * 1024;
        private static readonly Regex Symbols = new Regex(@"""(?:[^""]|"""")+""(?:\.(?:""(?:[^""]|"""")+""|[\p{L}_][\p{L}\p{Nd}_]*))*|#[\p{L}_][\p{L}\p{Nd}_]*(?:\.[\p{L}_][\p{L}\p{Nd}_]*)*", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly Regex Calls = new Regex(@"(?<![#\p{L}\p{Nd}_.])(?<name>""(?:[^""]|"""")+""|[\p{L}_][\p{L}\p{Nd}_]*)(?=\s*\()", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        private static readonly HashSet<string> Keywords = new HashSet<string>(new[] { "IF", "ELSIF", "WHILE", "FOR", "CASE", "NOT", "AND", "OR", "XOR", "MOD" }, StringComparer.OrdinalIgnoreCase);

        public static ProjectAnalysis Analyze(string directory, ExportSnapshot snapshot, CancellationToken cancellation, AnalysisOptions options = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            snapshot.Metrics = snapshot.Metrics ?? new ExportMetrics();
            snapshot.Metrics.AnalysisCacheHits = 0; snapshot.Metrics.AnalysisCacheMisses = 0;
            snapshot.Metrics.ManagedBytesBefore = GC.GetTotalMemory(false);
            try
            {
                ProjectAnalysis result;
                if (options == null) result = AnalyzeUncached(directory, snapshot, cancellation, true);
                else
                {
                    result = new ProjectAnalysis();
                    var cache = string.IsNullOrWhiteSpace(options.CacheDirectory) ? null : new AnalysisCache(options.CacheDirectory);
                    foreach (var artifact in snapshot.Artifacts.Where(a => a.RelativePath != null))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string warning = null; AnalysisFragment fragment = null;
                        var hash = SnapshotStore.FileHash(ExportPaths.Resolve(directory, artifact.RelativePath));
                        if (cache != null && cache.Restore(directory, artifact, hash, options.IncludeNetworkXml, cancellation, out fragment, out warning))
                            snapshot.Metrics.AnalysisCacheHits++;
                        else
                        {
                            snapshot.Metrics.AnalysisCacheMisses++;
                            if (warning != null) { snapshot.Metrics.CacheWarnings++; snapshot.Notes.Add(warning); }
                            var single = new ExportSnapshot { Artifacts = new List<ExportArtifact> { artifact }, IsDemo = snapshot.IsDemo };
                            fragment = new AnalysisFragment { Version = AnalysisCache.ParserVersion,
                                Analysis = AnalyzeUncached(directory, single, cancellation, options.IncludeNetworkXml), ContentHash = artifact.ContentSha256 };
                            if (cache != null && fragment.Analysis.Blocks.Count == 1 && !fragment.Analysis.Warnings.Any(w => w.Contains("analisi incompleta")))
                                try { cache.Save(directory, artifact, hash, options.IncludeNetworkXml, fragment, cancellation); }
                                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                                { snapshot.Metrics.CacheWarnings++; snapshot.Notes.Add("Cache analisi non aggiornata: " + ex.Message); }
                        }
                        artifact.ContentSha256 = fragment.ContentHash;
                        result.Blocks.AddRange(fragment.Analysis.Blocks); result.Symbols.AddRange(fragment.Analysis.Symbols);
                        result.Dependencies.AddRange(fragment.Analysis.Dependencies); result.Warnings.AddRange(fragment.Analysis.Warnings);
                    }
                    ResolveDependencies(result);
                    result.Warnings = result.Warnings.Distinct(StringComparer.Ordinal).ToList();
                }
                result.CrossReferences.AddRange(snapshot.CrossReferences ?? new List<PlcCrossReference>());
                foreach (var reference in result.CrossReferences)
                    result.Symbols.Add(new SymbolEvidence { ArtifactId = reference.QueriedArtifactId, Name = reference.SourceName,
                        Block = reference.ReferenceName, Plc = reference.Plc, Project = reference.Project, File = "crossreferences.json", Network = "",
                        Location = reference.Location, Address = reference.SourceAddress, Access = reference.Access, Evidence = reference.Provider + " CrossReference" });
                return result;
            }
            finally
            {
                snapshot.Metrics.AnalysisMilliseconds = timer.ElapsedMilliseconds;
                snapshot.Metrics.ManagedBytesAfter = GC.GetTotalMemory(false);
            }
        }
        private static ProjectAnalysis AnalyzeUncached(string directory, ExportSnapshot snapshot, CancellationToken cancellation, bool includeXml)
        {
            var analysis = new ProjectAnalysis();
            foreach (var artifact in snapshot.Artifacts.Where(a => a.RelativePath != null))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var document = Load(ExportPaths.Resolve(directory, artifact.RelativePath));
                    artifact.ContentSha256 = ContentHash(document);
                    var block = new BlockAnalysis { ArtifactId = artifact.Id, Name = artifact.Name, File = artifact.RelativePath,
                        Project = artifact.Project, Plc = artifact.Plc, Unit = Unit(artifact.Group), Kind = artifact.Kind };
                    analysis.Blocks.Add(block);
                    var declarations = ReadDeclarations(document, block, analysis);
                    var instance = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "InstanceOfName");
                    if (instance != null && !string.IsNullOrWhiteSpace(instance.Value))
                        AddDependency(analysis, block, instance.Value, "InstanceOf", "", "XML InstanceOfName");
                    foreach (var unit in document.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit"))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var source = unit.Descendants().FirstOrDefault(e => e.Name.LocalName == "NetworkSource");
                        if (source == null) continue;
                        var structured = source.Descendants().FirstOrDefault(e => e.Name.LocalName == "StructuredText");
                        var network = new NetworkEvidence { Id = (string)unit.Attribute("ID") ?? (block.Networks.Count + 1).ToString(),
                            Language = AttributeValue(unit, "ProgrammingLanguage") ?? artifact.Language,
                            Source = structured == null ? null : Render(structured),
                            GraphXml = includeXml ? source.ToString(SaveOptions.DisableFormatting) : null };
                        block.Networks.Add(network);
                        foreach (var call in source.Descendants().Where(e => e.Name.LocalName == "CallInfo"))
                        {
                            var name = (string)call.Attribute("Name");
                            if (string.IsNullOrEmpty(name))
                            {
                                var instanceAccess = call.Elements().FirstOrDefault(e => e.Name.LocalName == "Instance");
                                if (instanceAccess != null)
                                {
                                    name = string.Join(".", instanceAccess.Elements().Where(e => e.Name.LocalName == "Component").Select(e => (string)e.Attribute("Name")));
                                    if ((string)instanceAccess.Attribute("Scope") == "LocalVariable")
                                    {
                                        string datatype;
                                        name = declarations.TryGetValue(name, out datatype) && datatype != null ? datatype.Trim('"') : "#" + name;
                                    }
                                }
                            }
                            AddDependency(analysis, block, name, "Call", network.Id, "XML CallInfo");
                        }
                        foreach (var access in source.Descendants().Where(e => e.Name.LocalName == "Access"))
                        {
                            var name = SymbolName(access);
                            if (name.Length > 0) AddSymbol(analysis, block, network.Id, name, (string)access.Attribute("Scope"), "XML Access", null, null);
                        }
                        if (network.Source != null)
                        {
                            var masked = MaskCommentsAndStrings(network.Source);
                            foreach (Match match in Symbols.Matches(masked))
                                AddSymbol(analysis, block, network.Id, match.Value, match.Value.StartsWith("#") ? "LocalVariable" : "GlobalVariable", "SCL textual reference", null, null);
                            foreach (Match match in Calls.Matches(masked))
                            {
                                var name = match.Groups["name"].Value.Trim('"');
                                if (!Keywords.Contains(name)) AddDependency(analysis, block, name, "Call", network.Id, "SCL call candidate");
                            }
                        }
                    }
                    if (artifact.Protected) analysis.Warnings.Add(artifact.Name + ": interfaccia protetta, logica interna non disponibile.");
                    if (block.Networks.Count == 0 && new[] { "OB", "FB", "FC" }.Contains(artifact.Kind))
                        analysis.Warnings.Add(artifact.Name + ": nessuna rete leggibile; non dedurre l'assenza di logica.");
                }
                catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException || ex is RegexMatchTimeoutException)
                {
                    analysis.Warnings.Add(artifact.RelativePath + ": analisi incompleta: " + ex.Message);
                }
            }
            ResolveDependencies(analysis);
            analysis.Warnings.Add("Riferimenti XML e candidati SCL sono evidenze statiche; i cross-reference Siemens, quando disponibili, sono conservati separatamente.");
            analysis.Warnings.Add("Il testo SCL è una ricostruzione dei token: per sintassi non riconosciuta, accessi indicizzati e multi-istanze fare riferimento al NetworkSource XML originale.");
            analysis.Warnings.Add("LAD/FBD/GRAPH richiedono analisi della rete completa: XML originali sempre presenti; nessun valore runtime disponibile.");
            return analysis;
        }
        private static void ResolveDependencies(ProjectAnalysis analysis)
        {
            var byId = analysis.Blocks.ToDictionary(b => b.ArtifactId);
            var byName = analysis.Blocks.ToLookup(b => Tuple.Create(b.Project, b.Plc, b.Unit, b.Name.ToUpperInvariant()));
            foreach (var dependency in analysis.Dependencies)
            {
                var owner = byId[dependency.FromId];
                dependency.TargetId = null;
                var candidates = byName[Tuple.Create(owner.Project, owner.Plc, owner.Unit, dependency.TargetName.ToUpperInvariant())].Where(b => b.Kind != "TagTable" &&
                    (dependency.Relation == "Type" ? b.Kind == "UDT" : b.Kind != "UDT")).ToList();
                dependency.Resolution = candidates.Count == 1 ? "Resolved" : candidates.Count > 1 ? "Ambiguous" : "ExternalOrUnresolved";
                if (candidates.Count == 1) dependency.TargetId = candidates[0].ArtifactId;
            }
        }
        public static IReadOnlyList<SymbolEvidence> Search(ProjectAnalysis analysis, string query)
        {
            if (analysis == null || string.IsNullOrWhiteSpace(query)) return new List<SymbolEvidence>();
            return analysis.Symbols.Where(s => (s.Name ?? "").IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                (s.Address ?? "").IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList().AsReadOnly();
        }
        private static XDocument Load(string path)
        {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = MaxXmlCharacters }))
                return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        private static string ContentHash(XDocument document)
        {
            var normalized = new XDocument(document);
            if (normalized.Root != null)
                normalized.Root.Elements().Where(e => e.Name.LocalName == "DocumentInfo").Remove();
            // Preserve whitespace in leaf source tokens; discard only XML indentation.
            foreach (var text in normalized.DescendantNodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value) && (t.Parent == null || t.Parent.HasElements)).ToList()) text.Remove();
            return ExportPaths.Hash(normalized.ToString(SaveOptions.DisableFormatting));
        }
        private static string Unit(string group)
        {
            if (group == null || !group.StartsWith("Units/", StringComparison.Ordinal)) return "";
            var end = group.IndexOf('/', 6); return end < 0 ? group.Substring(6) : group.Substring(6, end - 6);
        }
        private static string AttributeValue(XElement element, string name)
        {
            return element.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList")?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        }
        private static Dictionary<string, string> ReadDeclarations(XDocument document, BlockAnalysis block, ProjectAnalysis analysis)
        {
            var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in document.Descendants().Where(e => e.Name.LocalName == "Member"))
            {
                var names = member.AncestorsAndSelf().Where(e => e.Name.LocalName == "Member").Reverse().Select(e => (string)e.Attribute("Name"));
                var section = member.Ancestors().FirstOrDefault(e => e.Name.LocalName == "Section");
                var name = string.Join(".", names); declarations[name] = (string)member.Attribute("Datatype");
                AddSymbol(analysis, block, "", name, (string)section?.Attribute("Name"), "Declaration", (string)member.Attribute("Datatype"), null);
                foreach (Match type in Symbols.Matches((string)member.Attribute("Datatype") ?? ""))
                    if (type.Value.StartsWith("\"")) AddDependency(analysis, block, type.Value.Trim('"').Replace("\"\"", "\""), "Type", "", "XML Datatype");
            }
            foreach (var tag in document.Descendants().Where(e => e.Name.LocalName == "SW.Tags.PlcTag" || e.Name.LocalName == "SW.Tags.PlcConstant"))
                AddSymbol(analysis, block, "", AttributeValue(tag, "Name"), "GlobalVariable", "Tag declaration", AttributeValue(tag, "DataTypeName"), AttributeValue(tag, "LogicalAddress"));
            return declarations;
        }
        private static void AddSymbol(ProjectAnalysis analysis, BlockAnalysis block, string network, string name, string scope, string evidence, string dataType, string address)
        {
            if (string.IsNullOrEmpty(name) || !analysis.SymbolKeys.Add(string.Join("\0", new[] { block.ArtifactId, network, name, evidence }))) return;
            analysis.Symbols.Add(new SymbolEvidence { ArtifactId = block.ArtifactId, Block = block.Name, File = block.File, Project = block.Project,
                Plc = block.Plc, Network = network, Name = name, Scope = scope, Evidence = evidence, DataType = dataType, Address = address });
        }
        private static void AddDependency(ProjectAnalysis analysis, BlockAnalysis block, string name, string relation, string network, string evidence)
        {
            if (string.IsNullOrWhiteSpace(name) || !analysis.DependencyKeys.Add(string.Join("\0", new[] { block.ArtifactId, name, network, relation }))) return;
            analysis.Dependencies.Add(new DependencyEvidence { FromId = block.ArtifactId, TargetName = name, Relation = relation, Network = network, Evidence = evidence });
        }
        private static string SymbolName(XElement access)
        {
            var symbol = access.Elements().FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (symbol == null) return (string)access.Elements().FirstOrDefault(e => e.Name.LocalName == "Constant")?.Attribute("Name") ?? "";
            return string.Join(".", symbol.Elements().Where(e => e.Name.LocalName == "Component").Select(e => (string)e.Attribute("Name")));
        }
        private static string Render(XElement element)
        {
            var text = new StringBuilder(); RenderNode(element, text); return text.ToString();
        }
        private static void RenderNode(XElement element, StringBuilder text)
        {
            switch (element.Name.LocalName)
            {
                case "Text": text.Append(element.Value); return;
                case "Token": text.Append((string)element.Attribute("Text") ?? (string)element.Attribute("text") ?? element.Value); return;
                case "Blank": text.Append(' ', Count(element, 1024)); return;
                case "NewLine": text.Append('\n', Count(element, 128)); return;
                case "LineComment":
                    bool inserted = string.Equals((string)element.Attribute("Inserted"), "true", StringComparison.OrdinalIgnoreCase);
                    var comment = string.Concat(element.Descendants().Where(e => e.Name.LocalName == "Text").Select(e => e.Value));
                    text.Append(inserted ? "(*" : "//").Append(inserted ? comment : comment.Replace("\r\n", "\n").Replace("\n", "\n//")).Append(inserted ? "*)" : ""); return;
                case "BlockComment": case "MultiLineComment":
                    text.Append("(*").Append(string.Concat(element.Descendants().Where(e => e.Name.LocalName == "Text").Select(e => e.Value))).Append("*)"); return;
                case "CallInfo": case "Instruction":
                    var instance = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Instance");
                    if (instance != null) RenderSymbol(instance, text, (string)instance.Attribute("Scope"));
                    else text.Append(element.Name.LocalName == "Instruction" ? (string)element.Attribute("Name") : Quote((string)element.Attribute("Name")));
                    bool explicitBrackets = element.Elements().Any(e => e.Name.LocalName == "Token" && ((string)e.Attribute("Text") ?? (string)e.Attribute("text")) == "(");
                    if (!explicitBrackets) text.Append('(');
                    bool firstParameter = true;
                    foreach (var item in element.Elements().Where(e => e != instance && !e.Name.LocalName.EndsWith("Attribute", StringComparison.Ordinal) && e.Name.LocalName != "TemplateValue"))
                    {
                        if (!explicitBrackets && (item.Name.LocalName == "Parameter" || item.Name.LocalName == "NamelessParameter"))
                        { if (!firstParameter) text.Append(", "); firstParameter = false; }
                        RenderNode(item, text);
                    }
                    if (!explicitBrackets) text.Append(')'); return;
                case "Parameter":
                    if (string.Equals((string)element.Attribute("Informative"), "true", StringComparison.OrdinalIgnoreCase)) return;
                    text.Append((string)element.Attribute("Name")); break;
                case "Access":
                    if ((string)element.Attribute("Scope") == "Call") break;
                    var name = SymbolName(element);
                    if (name.Length > 0)
                    {
                        var scope = (string)element.Attribute("Scope");
                        var symbol = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Symbol");
                        if (symbol != null) RenderSymbol(symbol, text, scope);
                        else text.Append(scope == "LocalConstant" ? "#" + name : Quote(name));
                    }
                    else
                    {
                        var value = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "ConstantValue" || e.Name.LocalName == "Value");
                        var type = element.Descendants().FirstOrDefault(e => e.Name.LocalName.StartsWith("ConstantType", StringComparison.Ordinal))?.Value;
                        var literal = value?.Value ?? element.Value;
                        if ((string.Equals(type, "String", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "WString", StringComparison.OrdinalIgnoreCase)) && !literal.StartsWith("'"))
                            literal = (string.Equals(type, "WString", StringComparison.OrdinalIgnoreCase) ? "WSTRING#" : "") + "'" +
                                literal.Replace("$", "$$").Replace("'", "$'").Replace("\r", "$R").Replace("\n", "$N").Replace("\t", "$T") + "'";
                        text.Append(literal);
                    }
                    return;
            }
            foreach (var node in element.Nodes())
            {
                var child = node as XElement;
                if (child != null) RenderNode(child, text);
                else if (node is XText && !string.IsNullOrWhiteSpace(((XText)node).Value)) text.Append(((XText)node).Value);
            }
        }
        private static string Quote(string name) { return "\"" + (name ?? "").Replace("\"", "\"\"") + "\""; }
        private static void RenderSymbol(XElement symbol, StringBuilder text, string scope)
        {
            bool local = scope == "LocalVariable" || scope == "LocalConstant";
            if (local) text.Append('#');
            bool first = true;
            foreach (var component in symbol.Elements().Where(e => e.Name.LocalName == "Component"))
            {
                if (!first) text.Append('.'); first = false;
                var name = (string)component.Attribute("Name"); text.Append(local ? name : Quote(name));
                foreach (var index in component.Elements().Where(e => e.Name.LocalName == "Access"))
                { text.Append('['); RenderNode(index, text); text.Append(']'); }
            }
        }
        private static int Count(XElement element, int max)
        {
            int value; return int.TryParse((string)element.Attribute("Num"), out value) ? Math.Max(0, Math.Min(max, value)) : 1;
        }
        internal static string MaskCommentsAndStrings(string source)
        {
            var result = source.ToCharArray(); int index = 0;
            while (index < source.Length)
            {
                // Double quotes denote identifiers, so comment markers within them stay literal.
                if (source[index] == '"')
                {
                    index++;
                    while (index < source.Length)
                    {
                        if (source[index++] != '"') continue;
                        if (index < source.Length && source[index] == '"') { index++; continue; }
                        break;
                    }
                    continue;
                }
                int start = index;
                if (source[index] == '\'')
                {
                    index++;
                    while (index < source.Length)
                    {
                        if (source[index] == '$' && index + 1 < source.Length) { index += 2; continue; }
                        if (source[index++] != '\'') continue;
                        if (index < source.Length && source[index] == '\'') { index++; continue; }
                        break;
                    }
                }
                else if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '/')
                { while (index < source.Length && source[index] != '\n') index++; }
                else if (index + 1 < source.Length && source[index] == '(' && source[index + 1] == '*')
                {
                    index += 2; int depth = 1;
                    while (index < source.Length && depth > 0)
                    {
                        if (index + 1 < source.Length && source[index] == '(' && source[index + 1] == '*') { depth++; index += 2; }
                        else if (index + 1 < source.Length && source[index] == '*' && source[index + 1] == ')') { depth--; index += 2; }
                        else index++;
                    }
                }
                else { index++; continue; }
                for (int i = start; i < index; i++) if (result[i] != '\n' && result[i] != '\r') result[i] = ' ';
            }
            return new string(result);
        }
    }
}
