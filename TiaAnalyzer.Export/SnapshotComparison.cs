using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    public sealed class SnapshotChange
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Plc { get; set; }
        public string Change { get; set; }
        public string BeforeFile { get; set; }
        public string AfterFile { get; set; }
    }
    public sealed class SnapshotComparison
    {
        public string Before { get; set; }
        public string After { get; set; }
        public List<SnapshotChange> Changes { get; set; } = new List<SnapshotChange>();
        public List<string> Warnings { get; set; } = new List<string>();
        public int Unchanged { get; set; }
    }
    public static class SnapshotComparer
    {
        private const long MaxManifestBytes = 64L * 1024 * 1024;
        public static SnapshotComparison Compare(string before, string after)
        {
            var previous = ReadManifest(before); var current = ReadManifest(after);
            if (previous.IsDemo != current.IsDemo) throw new InvalidDataException("Non confrontare dati DEMO con un progetto reale.");
            var result = new SnapshotComparison { Before = before, After = after };
            var oldFiles = previous.Artifacts.ToDictionary(a => a.Id, StringComparer.Ordinal);
            var newFiles = current.Artifacts.ToDictionary(a => a.Id, StringComparer.Ordinal);
            var oldScopes = new HashSet<string>(previous.Artifacts.Select(a => a.Project + "\0" + a.Plc));
            if (!oldScopes.Overlaps(current.Artifacts.Select(a => a.Project + "\0" + a.Plc)))
                result.Warnings.Add("Nessuno scope progetto/PLC in comune: potrebbero essere progetti diversi oppure nomi modificati.");
            if (previous.Issues.Count > 0 || current.Issues.Count > 0 || previous.Artifacts.Any(a => a.Status != "Exported") || current.Artifacts.Any(a => a.Status != "Exported"))
                result.Warnings.Add("Almeno uno snapshot contiene errori o oggetti protetti. Aggiunte e rimozioni non dimostrano una modifica del programma senza verificare la copertura.");
            result.Warnings.Add("Il confronto usa identità e hash dichiarati nei manifest; non verifica gli XML allegati, non è un confronto semantico PLC e non prova equivalenza funzionale.");
            foreach (var id in oldFiles.Keys.Union(newFiles.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                ExportArtifact oldFile, newFile;
                oldFiles.TryGetValue(id, out oldFile); newFiles.TryGetValue(id, out newFile);
                string change;
                if (oldFile == null) change = "Added";
                else if (newFile == null) change = "Removed";
                else if (oldFile.Status != newFile.Status) change = "AvailabilityChanged";
                else if (oldFile.Status == "Failed") change = "Unavailable";
                else if (oldFile.Sha256 == newFile.Sha256) { result.Unchanged++; continue; }
                else if (oldFile.ContentSha256 != null && oldFile.ContentSha256 == newFile.ContentSha256) change = "ExportMetadataOnly";
                else change = "Changed";
                var artifact = newFile ?? oldFile;
                result.Changes.Add(new SnapshotChange { Id = id, Name = artifact.Name, Plc = artifact.Plc, Change = change,
                    BeforeFile = oldFile?.RelativePath, AfterFile = newFile?.RelativePath });
            }
            return result;
        }
        public static ExportSnapshot ReadManifest(string path)
        {
            ExportSnapshot snapshot;
            using (var file = File.OpenRead(path))
            {
                if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
                    {
                        if (archive.Entries.Count > 100000) throw new InvalidDataException("Archivio troppo grande.");
                        var manifests = archive.Entries.Where(e => e.FullName == "manifest.json").ToList();
                        if (manifests.Count != 1 || manifests[0].Length > MaxManifestBytes) throw new InvalidDataException("Manifest assente, duplicato o troppo grande.");
                        using (var entry = manifests[0].Open()) snapshot = ReadBounded(entry);
                    }
                }
                else
                {
                    if (file.Length > MaxManifestBytes) throw new InvalidDataException("Manifest troppo grande.");
                    snapshot = ReadBounded(file);
                }
            }
            Validate(snapshot); return snapshot;
        }
        private static ExportSnapshot ReadBounded(Stream input)
        {
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[81920]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (memory.Length + count > MaxManifestBytes) throw new InvalidDataException("Manifest decompresso troppo grande.");
                    memory.Write(buffer, 0, count);
                }
                memory.Position = 0;
                return JsonFiles.Read<ExportSnapshot>(memory);
            }
        }
        private static bool IsHash(string value)
        {
            return value != null && value.Length == 64 && value.All(ch => (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'));
        }
        private static void Validate(ExportSnapshot snapshot)
        {
            if (snapshot == null || snapshot.SchemaVersion != "1.0" || snapshot.Artifacts == null || snapshot.Issues == null || snapshot.Artifacts.Count > 100000)
                throw new InvalidDataException("Schema manifest non supportato o incompleto.");
            var ids = new HashSet<string>();
            foreach (var artifact in snapshot.Artifacts)
            {
                if (artifact == null || !IsHash(artifact.Id) || !ids.Add(artifact.Id) || string.IsNullOrEmpty(artifact.Name) || string.IsNullOrEmpty(artifact.Project) ||
                    string.IsNullOrEmpty(artifact.Plc) || !new[] { "Exported", "InterfaceOnly", "Failed" }.Contains(artifact.Status))
                    throw new InvalidDataException("Oggetto manifest non valido o duplicato.");
                if (artifact.Status != "Failed" && (!IsHash(artifact.Sha256) || string.IsNullOrWhiteSpace(artifact.RelativePath)))
                    throw new InvalidDataException("Oggetto esportato senza file o hash.");
                if (artifact.ContentSha256 != null && !IsHash(artifact.ContentSha256)) throw new InvalidDataException("Hash contenuto non valido.");
                if (artifact.RelativePath != null) ExportPaths.Resolve(Path.GetTempPath(), artifact.RelativePath);
            }
        }
        public static string Markdown(SnapshotComparison comparison)
        {
            var text = new StringBuilder("# Confronto snapshot PLC\n\n");
            text.Append("Prima: ").AppendLine(PackageWriter.Text(comparison.Before));
            text.Append("\nDopo: ").AppendLine(PackageWriter.Text(comparison.After));
            text.Append("\nOggetti invariati: ").Append(comparison.Unchanged).AppendLine(".\n");
            foreach (var warning in comparison.Warnings) text.Append("- ").AppendLine(PackageWriter.Text(warning));
            text.AppendLine("\n## Differenze\n");
            foreach (var change in comparison.Changes)
                text.Append("- ").Append(PackageWriter.Text(change.Change)).Append(" · ").Append(PackageWriter.Text(change.Plc))
                    .Append(" / ").Append(PackageWriter.Text(change.Name)).Append(" · ").AppendLine(PackageWriter.Text(change.Id));
            return text.ToString();
        }
    }
}
