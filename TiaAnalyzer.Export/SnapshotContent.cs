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
        public SnapshotContent(string snapshotPath)
        {
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
        private static void CheckHash(Stream stream, ExportArtifact artifact, CancellationToken cancellation, Stream copy)
        {
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920]; long bytes = 0; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested(); bytes += count;
                    if (bytes > MaxXmlBytes) throw new InvalidDataException("XML troppo grande per il confronto: " + artifact.Name);
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
            using (var stream = File.OpenRead(ExportPaths.Resolve(Path.GetDirectoryName(Path.GetFullPath(snapshotPath)), artifact.RelativePath))) return action(stream);
        }
    }
}
