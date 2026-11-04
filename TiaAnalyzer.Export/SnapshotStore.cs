using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    public static class JsonFiles
    {
        public static void Write<T>(string path, T value)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
        }
        public static T Read<T>(Stream stream)
        {
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
    public static class SnapshotStore
    {
        public static string FileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void Complete(string directory, ExportSnapshot snapshot, CancellationToken cancellation)
        {
            foreach (var artifact in snapshot.Artifacts)
            {
                cancellation.ThrowIfCancellationRequested();
                if (artifact.RelativePath == null) continue;
                var path = ExportPaths.Resolve(directory, artifact.RelativePath);
                artifact.Sha256 = FileHash(path);
                artifact.Bytes = new FileInfo(path).Length;
            }
            cancellation.ThrowIfCancellationRequested();
            JsonFiles.Write(Path.Combine(directory, "components.json"), snapshot.Components);
            JsonFiles.Write(Path.Combine(directory, "manifest.json"), snapshot);
        }
    }
}
