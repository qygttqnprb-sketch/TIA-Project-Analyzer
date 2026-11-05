using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;

namespace TiaAnalyzer.Core
{
    public sealed class CheckpointEntry
    {
        public ExportArtifact Artifact { get; set; }
        public string SourceIdentity { get; set; }
        public string SourceProof { get; set; }
        public string ApiIdentity { get; set; }
        public string Stamp { get; set; }
    }
    public sealed class ExportCheckpoint
    {
        public string Version { get; set; } = "1.0";
        public string ExportId { get; set; }
        public string State { get; set; } = "Incomplete";
        public string EntryDirectory { get; set; }
        public List<CheckpointEntry> Entries { get; set; } = new List<CheckpointEntry>();
    }
    public static class CheckpointStore
    {
        public static void Write<T>(string path, T value)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static ExportCheckpoint Read(string directory, bool loadEntries = true)
        {
            var path = ExportPaths.Resolve(directory, "checkpoint.json"); CheckLinks(directory, path);
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > 64L * 1024 * 1024) throw new InvalidDataException("Checkpoint troppo grande.");
                var value = (ExportCheckpoint)new DataContractJsonSerializer(typeof(ExportCheckpoint)).ReadObject(stream);
                if (value != null && value.EntryDirectory != null && loadEntries)
                {
                    if (value.EntryDirectory != "checkpoint-entries") throw new InvalidDataException("Directory checkpoint non valida.");
                    var entriesPath = Path.Combine(directory, value.EntryDirectory); CheckLinks(directory, entriesPath);
                    value.Entries = new List<CheckpointEntry>();
                    if (Directory.Exists(entriesPath))
                    foreach (var record in Directory.EnumerateFiles(entriesPath, "*.json"))
                    {
                        CheckLinks(directory, record);
                        using (var input = File.OpenRead(record))
                        {
                            if (input.Length > 1024 * 1024 || value.Entries.Count >= 100000) throw new InvalidDataException("Limite checkpoint superato.");
                            var entry = (CheckpointEntry)new DataContractJsonSerializer(typeof(CheckpointEntry)).ReadObject(input);
                            if (entry?.Artifact?.Id == null || Path.GetFileName(record) != ExportPaths.Hash(entry.Artifact.Id) + ".json") throw new InvalidDataException("Identità record checkpoint non valida.");
                            value.Entries.Add(entry);
                        }
                    }
                }
                if (value == null || value.Version != "1.0" || value.Entries == null || value.Entries.Count > 100000 ||
                    value.Entries.Any(e => e?.Artifact?.Id == null) || value.Entries.Select(e => e.Artifact.Id).Distinct().Count() != value.Entries.Count)
                    throw new InvalidDataException("Checkpoint non valido.");
                return value;
            }
        }
        public static void PutEntry(string directory, CheckpointEntry entry)
        {
            var entries = Path.Combine(directory, "checkpoint-entries"); Directory.CreateDirectory(entries);
            Write(Path.Combine(entries, ExportPaths.Hash(entry.Artifact.Id) + ".json"), entry);
        }
        public static string FileHash(string path, CancellationToken cancellation)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                { cancellation.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, count, buffer, 0); }
                cancellation.ThrowIfCancellationRequested(); sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        public static void ValidateXml(string path, CancellationToken cancellation)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64L * 1024 * 1024 };
            using (var reader = XmlReader.Create(path, settings)) while (reader.Read()) cancellation.ThrowIfCancellationRequested();
        }
        public static bool Restore(string directory, CheckpointEntry entry, string destination, string identity, string proof, string api, string stamp, CancellationToken cancellation)
        {
            var a = entry.Artifact;
            if (string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(proof) || string.IsNullOrEmpty(api) || string.IsNullOrEmpty(stamp) ||
                entry.SourceIdentity != identity || entry.SourceProof != proof || entry.ApiIdentity != api || entry.Stamp != stamp ||
                a.RelativePath == null || a.RelativePath.Split('/').Any(part => part.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) ||
                (a.Status != "Exported" && a.Status != "InterfaceOnly") || a.Bytes <= 0 || a.Sha256 == null || a.Sha256.Length != 64) return false;
            var source = ExportPaths.Resolve(directory, a.RelativePath); CheckLinks(directory, source);
            if (!File.Exists(source) || new FileInfo(source).Length != a.Bytes || FileHash(source, cancellation) != a.Sha256) return false;
            File.Copy(source, destination, false);
            if (FileHash(destination, cancellation) != a.Sha256) { File.Delete(destination); return false; }
            ValidateXml(destination, cancellation); return true;
        }
        public static void CheckLinks(string directory, string path)
        {
            var root = Path.GetFullPath(directory);
            for (var current = path; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Link o junction non ammessi per la ripresa.");
                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            }
        }
    }
}
