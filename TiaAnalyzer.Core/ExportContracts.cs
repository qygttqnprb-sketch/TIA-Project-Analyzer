using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Runtime.Serialization;

namespace TiaAnalyzer.Core
{
    public sealed class ExportArtifact
    {
        public string Id { get; set; }
        public string Project { get; set; }
        public string Plc { get; set; }
        public string Group { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Language { get; set; }
        public int? Number { get; set; }
        public string RelativePath { get; set; }
        public string Status { get; set; }
        public string Sha256 { get; set; }
        public string ContentSha256 { get; set; }
        public long Bytes { get; set; }
        public bool Protected { get; set; }
    }
    public sealed class ExportIssue
    {
        public string Source { get; set; }
        public string Message { get; set; }
    }
    [DataContract]
    public sealed class ExportSnapshot
    {
        [DataMember(IsRequired = true)]
        public string SchemaVersion { get; set; } = "1.0";
        [DataMember(IsRequired = true)]
        public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
        [DataMember(IsRequired = true)]
        public bool IsDemo { get; set; }
        [DataMember]
        public string Question { get; set; }
        [DataMember(IsRequired = true)]
        public List<ExportArtifact> Artifacts { get; set; } = new List<ExportArtifact>();
        [DataMember(IsRequired = true)]
        public List<ExportIssue> Issues { get; set; } = new List<ExportIssue>();
        [DataMember]
        public List<ProjectComponent> Components { get; set; } = new List<ProjectComponent>();
        [DataMember]
        public List<string> Limitations { get; set; } = new List<string>();
    }
    public sealed class PackageDiagnostics
    {
        public List<EnvironmentCheck> Environment { get; set; } = new List<EnvironmentCheck>();
        public List<string> Log { get; set; } = new List<string>();
    }
    public sealed class ExportProgress
    {
        public int Completed { get; set; }
        public string Message { get; set; }
    }
    // Only file export from an already attached session; no import/write-to-project APIs.
    public interface IPlcExportSession
    {
        ExportSnapshot ExportPlc(string directory, IProgress<ExportProgress> progress, CancellationToken cancellation);
    }
    public static class ExportPaths
    {
        public static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "").ToLowerInvariant();
        }
        public static string Identity(params string[] parts)
        {
            var value = new StringBuilder();
            foreach (var part in parts) { var item = part ?? ""; value.Append(item.Length).Append(':').Append(item); }
            return Hash(value.ToString());
        }
        public static string Segment(string name)
        {
            var safe = new StringBuilder();
            foreach (char ch in name ?? "unnamed")
            {
                if (safe.Length == 18) break;
                safe.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            }
            // Prefix prevents Windows device names; hash disambiguates sanitized names and case.
            return "x_" + safe + "_" + Hash(name).Substring(0, 12);
        }
        public static string Resolve(string directory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
                relativePath.Contains(":") || relativePath.Contains("\\")) throw new IOException("Percorso relativo non valido.");
            foreach (var part in relativePath.Split('/'))
                if (part == "." || part == ".." || string.IsNullOrEmpty(part)) throw new IOException("Percorso relativo non valido.");
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Percorso esterno alla cartella di esportazione.");
            return path;
        }
    }
}
