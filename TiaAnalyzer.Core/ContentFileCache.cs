using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace TiaAnalyzer.Core
{
    // A single atomically published blob: version + payload SHA256 + payload bytes.
    // Content is streamed; corrupted or inaccessible cache entries are disposable.
    public sealed class ContentFileCache
    {
        private readonly string directory;
        public ContentFileCache(string directory) { this.directory = Path.GetFullPath(directory); }
        public string EntryPath(string key) { return Path.Combine(directory, ExportPaths.Hash(key) + ".blob"); }
        public bool Restore(string key, string destination, CancellationToken cancellation, out string warning)
        {
            warning = null; bool owned = false, success = false;
            var entry = EntryPath(key);
            if (!File.Exists(entry)) return false;
            try
            {
                using (var source = File.OpenRead(entry))
                {
                    if (ReadLine(source) != "TIA-CACHE-1") throw new InvalidDataException("Versione cache non valida.");
                    var expected = ReadLine(source);
                    if (expected.Length != 64) throw new InvalidDataException("Hash cache non valido.");
                    using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                    using (var sha = SHA256.Create())
                    {
                        owned = true;
                        Copy(source, target, sha, cancellation);
                        target.Flush(true);
                        if (Hex(sha.Hash) != expected) throw new InvalidDataException("Integrità cache non valida.");
                    }
                }
                success = true; return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            { warning = "Cache ignorata: " + ex.Message; return false; }
            finally { if (owned && !success && File.Exists(destination)) File.Delete(destination); }
        }
        public void Save(string key, string sourcePath, CancellationToken cancellation)
        {
            Directory.CreateDirectory(directory);
            var entry = EntryPath(key); var temporary = entry + "." + Guid.NewGuid().ToString("N") + ".partial";
            bool owned = false;
            try
            {
                using (var source = File.OpenRead(sourcePath))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
                using (var sha = SHA256.Create())
                {
                    owned = true;
                    var header = Encoding.ASCII.GetBytes("TIA-CACHE-1\n" + new string('0', 64) + "\n");
                    output.Write(header, 0, header.Length);
                    Copy(source, output, sha, cancellation);
                    output.Position = "TIA-CACHE-1\n".Length;
                    var hash = Encoding.ASCII.GetBytes(Hex(sha.Hash)); output.Write(hash, 0, hash.Length); output.Flush(true);
                }
                cancellation.ThrowIfCancellationRequested();
                if (File.Exists(entry)) File.Replace(temporary, entry, null); else File.Move(temporary, entry);
                owned = false;
            }
            finally { if (owned && File.Exists(temporary)) File.Delete(temporary); }
        }
        private static string ReadLine(Stream stream)
        {
            var line = new StringBuilder(); int value;
            while ((value = stream.ReadByte()) >= 0 && value != '\n')
            { if (line.Length == 128) throw new InvalidDataException("Header cache non valido."); line.Append((char)value); }
            return line.ToString();
        }
        private static void Copy(Stream source, Stream target, HashAlgorithm sha, CancellationToken cancellation)
        {
            var buffer = new byte[81920]; int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancellation.ThrowIfCancellationRequested(); target.Write(buffer, 0, count);
                sha.TransformBlock(buffer, 0, count, buffer, 0);
            }
            cancellation.ThrowIfCancellationRequested(); sha.TransformFinalBlock(new byte[0], 0, 0);
        }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
