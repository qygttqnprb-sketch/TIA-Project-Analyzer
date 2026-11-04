using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TiaAnalyzer.Export
{
    internal static class XmlEvidence
    {
        public static string Hash(XContainer tree, bool omitDocumentInfo, bool normalizeUids = false)
        {
            using (var sha = SHA256.Create())
            {
                using (var crypto = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
                using (var writer = new BinaryWriter(crypto, new UTF8Encoding(false), true))
                {
                    var ids = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var node in tree is XElement ? new[] { (XNode)tree } : tree.Nodes()) Write(node, writer, ids, omitDocumentInfo, normalizeUids, 0);
                }
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        public static string TokenHash(IEnumerable<string> tokens)
        {
            using (var sha = SHA256.Create())
            {
                using (var crypto = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
                using (var writer = new BinaryWriter(crypto, new UTF8Encoding(false), true))
                    foreach (var token in tokens) writer.Write(token);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        private static void Write(XNode node, BinaryWriter writer, Dictionary<string, string> ids, bool omitInfo, bool normalizeUids, int depth)
        {
            if (depth > 256) throw new XmlException("XML oltre il limite di profondità per l'analisi.");
            var element = node as XElement;
            if (element != null)
            {
                if (omitInfo && depth == 1 && element.Name.LocalName == "DocumentInfo" && element.Parent?.Name.LocalName == "Document") return;
                writer.Write("element"); writer.Write(element.Name.ToString());
                var attributes = element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToList(); writer.Write(attributes.Count);
                foreach (var attribute in attributes)
                {
                    writer.Write(attribute.Name.ToString()); var value = attribute.Value;
                    if (normalizeUids && attribute.Name == "UId")
                    { string normalized; if (!ids.TryGetValue(value, out normalized)) ids.Add(value, normalized = ids.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)); value = normalized; }
                    writer.Write(value);
                }
                foreach (var child in element.Nodes()) Write(child, writer, ids, omitInfo, normalizeUids, depth + 1);
                writer.Write("end"); return;
            }
            var text = node as XText;
            if (text != null)
            {
                if (string.IsNullOrWhiteSpace(text.Value) && (text.Parent == null || text.Parent.HasElements)) return;
                writer.Write("text"); writer.Write(text.Value); return;
            }
            writer.Write(node.NodeType.ToString()); writer.Write(node.ToString(SaveOptions.DisableFormatting));
        }
    }
}
