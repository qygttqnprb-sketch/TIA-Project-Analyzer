using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    internal sealed class SnapshotContent : IDisposable
    {
        private const long MaxXmlBytes = 128L * 1024 * 1024;
        private readonly string snapshotPath;
        private readonly FileStream file;
        private readonly ZipArchive archive;
        private readonly ILookup<string, ZipArchiveEntry> entries;
        private readonly long maximumTotalBytes;
        private long totalBytes;
        public SnapshotContent(string snapshotPath, long maximumTotalBytes = long.MaxValue)
        {
            this.maximumTotalBytes = maximumTotalBytes;
            this.snapshotPath = Path.GetFullPath(snapshotPath);
            if (!Path.GetExtension(snapshotPath).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return;
            file = File.OpenRead(snapshotPath);
            try
            {
                archive = new ZipArchive(file, ZipArchiveMode.Read);
                if (archive.Entries.Count > 100000) throw new InvalidDataException("Archivio troppo grande.");
                entries = archive.Entries.ToLookup(e => e.FullName, StringComparer.Ordinal);
            }
            catch { archive?.Dispose(); file.Dispose(); throw; }
        }
        public void Dispose() { archive?.Dispose(); file?.Dispose(); }
        public ExportSnapshot ReadManifest(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (archive == null)
            {
                using (var stream = File.OpenRead(snapshotPath)) return SnapshotComparer.ReadVerifiedManifest(stream, cancellation);
            }
            var manifests = entries["manifest.json"].ToList();
            if (manifests.Count != 1 || manifests[0].Length > 64L * 1024 * 1024)
                throw new InvalidDataException("Manifest assente, duplicato o troppo grande.");
            using (var stream = manifests[0].Open()) return SnapshotComparer.ReadVerifiedManifest(stream, cancellation);
        }
        public void VerifyAll(ExportSnapshot snapshot, CancellationToken cancellation)
        {
            foreach (var artifact in snapshot.Artifacts.Where(a => a.RelativePath != null && a.Status != "Failed"))
            {
                cancellation.ThrowIfCancellationRequested();
                Read(artifact, stream => { CheckHash(stream, artifact, cancellation, null); return true; });
            }
        }
        public XDocument LoadXml(ExportArtifact artifact, CancellationToken cancellation)
        {
            return Read(artifact, stream =>
            {
                using (var bytes = new MemoryStream())
                {
                    CheckHash(stream, artifact, cancellation, bytes); bytes.Position = 0;
                    using (var reader = XmlReader.Create(bytes, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null, MaxCharactersInDocument = 64L * 1024 * 1024 })) return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                }
            });
        }
        private void CheckHash(Stream stream, ExportArtifact artifact, CancellationToken cancellation, Stream copy)
        {
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920]; long bytes = 0; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested(); bytes += count;
                    if (count > maximumTotalBytes - totalBytes) throw new InvalidDataException("Limite di lettura XML complessivo raggiunto.");
                    totalBytes += count;
                    if (bytes > MaxXmlBytes) throw new InvalidDataException("XML troppo grande: " + artifact.Name);
                    sha.TransformBlock(buffer, 0, count, buffer, 0); copy?.Write(buffer, 0, count);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                var actual = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                if (actual != artifact.Sha256 || (artifact.Bytes > 0 && bytes != artifact.Bytes))
                    throw new InvalidDataException("XML non corrispondente al manifest: " + artifact.Name);
            }
        }
        private T Read<T>(ExportArtifact artifact, Func<Stream, T> action)
        {
            if (archive != null)
            {
                var matches = entries[artifact.RelativePath].ToList();
                if (matches.Count != 1 || matches[0].Length > MaxXmlBytes) throw new InvalidDataException("XML assente, duplicato o troppo grande: " + artifact.Name);
                using (var stream = matches[0].Open()) return action(stream);
            }
            var root = Path.GetDirectoryName(snapshotPath);
            var path = ExportPaths.Resolve(root, artifact.RelativePath);
            for (var current = path; !string.Equals(current, root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Link o junction non consentito nel contenuto dello snapshot.");
            using (var stream = File.OpenRead(path)) return action(stream);
        }
    }
}
