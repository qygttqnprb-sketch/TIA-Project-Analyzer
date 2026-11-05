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
        public static void WriteAtomic<T>(string path, T value, CancellationToken cancellation)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
            bool owned = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    owned = true;
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                    { new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value); writer.Flush(); }
                    stream.Flush(true);
                }
                cancellation.ThrowIfCancellationRequested();
                File.Move(temporary, path); owned = false;
            }
            finally { if (owned && File.Exists(temporary)) File.Delete(temporary); }
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
        public static void Complete(string directory, ExportSnapshot snapshot, CancellationToken cancellation, bool writeMetrics = true)
        {
            if (snapshot.AcquisitionState == "Cancelled" || snapshot.AcquisitionState == "InProgress")
                throw new InvalidDataException("Snapshot annullato o ancora in corso: impossibile completarlo.");
            foreach (var artifact in snapshot.Artifacts)
            {
                cancellation.ThrowIfCancellationRequested();
                if (artifact.RelativePath == null) continue;
                var path = ExportPaths.Resolve(directory, artifact.RelativePath);
                var actualHash = CheckpointStore.FileHash(path, cancellation);
                if (artifact.Sha256 != null && artifact.Sha256 != actualHash) throw new InvalidDataException("XML modificato dopo il checkpoint: " + artifact.Name);
                artifact.Sha256 = actualHash;
                artifact.Bytes = new FileInfo(path).Length;
            }
            cancellation.ThrowIfCancellationRequested();
            JsonFiles.Write(Path.Combine(directory, "components.json"), snapshot.Components);
            JsonFiles.Write(Path.Combine(directory, "crossreferences.json"), snapshot.CrossReferences ?? new System.Collections.Generic.List<PlcCrossReference>());
            if (writeMetrics) JsonFiles.Write(Path.Combine(directory, "metrics.json"), snapshot.Metrics ?? new ExportMetrics());
            if (snapshot.AcquisitionState == "Collected") snapshot.AcquisitionState = "Complete";
            JsonFiles.WriteAtomic(Path.Combine(directory, "manifest.json"), snapshot, cancellation);
            var checkpointPath = Path.Combine(directory, "checkpoint.json");
            if (File.Exists(checkpointPath))
            { var checkpoint = CheckpointStore.Read(directory, false); checkpoint.State = snapshot.AcquisitionState; CheckpointStore.Write(checkpointPath, checkpoint); }
        }
    }
}
