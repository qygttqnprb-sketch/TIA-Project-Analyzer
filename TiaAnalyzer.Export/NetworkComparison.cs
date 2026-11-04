using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace TiaAnalyzer.Export
{
    public sealed class NetworkDifference
    {
        public int Position { get; set; }
        public string BeforeId { get; set; }
        public string AfterId { get; set; }
        public string Change { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        public bool PreviewTruncated { get; set; }
    }
    internal static class NetworkComparison
    {
        private sealed class Network { public string Id, Hash, Source, Xml; }
        public static List<NetworkDifference> Compare(XDocument before, XDocument after, CancellationToken cancellation)
        {
            var oldNetworks = Read(before); var newNetworks = Read(after); var result = new List<NetworkDifference>();
            for (int i = 0; i < Math.Max(oldNetworks.Count, newNetworks.Count); i++)
            {
                cancellation.ThrowIfCancellationRequested(); var old = i < oldNetworks.Count ? oldNetworks[i] : null; var current = i < newNetworks.Count ? newNetworks[i] : null;
                if (old != null && current != null && old.Hash == current.Hash && old.Xml == current.Xml && old.Id == current.Id) continue;
                string change = old == null ? "AddedAtPosition" : current == null ? "RemovedAtPosition" :
                    old.Hash == current.Hash ? "RepresentationOrComments" : "ChangedAtPosition";
                var oldText = old?.Source ?? old?.Xml; var newText = current?.Source ?? current?.Xml;
                result.Add(new NetworkDifference { Position = i + 1, BeforeId = old?.Id, AfterId = current?.Id, Change = change,
                    Before = Preview(oldText), After = Preview(newText), PreviewTruncated = (oldText?.Length ?? 0) > 8000 || (newText?.Length ?? 0) > 8000 });
            }
            return result;
        }
        private static string Preview(string text) { return text == null || text.Length <= 8000 ? text : text.Substring(0, 8000); }
        private static List<Network> Read(XDocument document)
        {
            var result = new List<Network>();
            if (document == null) return result;
            foreach (var unit in document.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit"))
            {
                var source = unit.Descendants().FirstOrDefault(e => e.Name.LocalName == "NetworkSource"); if (source == null) continue;
                var scl = source.Descendants().FirstOrDefault(e => e.Name.LocalName == "StructuredText"); var text = scl == null ? null : XmlProjectAnalyzer.Render(scl);
                string hash;
                try { hash = text == null ? XmlEvidence.Hash(source, false, true) : XmlEvidence.TokenHash(SclLogicAnalyzer.Lex(text).Select(t => t.Text)); }
                catch (SclLogicAnalyzer.Unsupported) { hash = XmlEvidence.Hash(source, false, true); }
                result.Add(new Network { Id = (string)unit.Attribute("ID"), Source = text, Hash = hash, Xml = source.ToString(SaveOptions.DisableFormatting) });
            }
            return result;
        }
    }
}
