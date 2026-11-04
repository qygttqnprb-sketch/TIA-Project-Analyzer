using System;
using System.IO;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    // Shared by the real and demo adapters. Exceptions and cancellation never turn a
    // failed/partial XML into an apparently successful artifact.
    internal sealed class PlcExportWriter
    {
        private readonly string directory;
        private readonly IProgress<ExportProgress> progress;
        private readonly CancellationToken cancellation;
        public ExportSnapshot Snapshot { get; }
        public PlcExportWriter(string directory, bool demo, IProgress<ExportProgress> progress, CancellationToken cancellation)
        {
            this.directory = Path.GetFullPath(directory); this.progress = progress; this.cancellation = cancellation;
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length != 0)
                throw new IOException("Usare una cartella di esportazione nuova e vuota.");
            Directory.CreateDirectory(directory);
            Snapshot = new ExportSnapshot { IsDemo = demo };
            Snapshot.Limitations.Add("Snapshot offline sequenziale: non contiene valori runtime e non è una transazione atomica del progetto.");
            Snapshot.Limitations.Add("I blocchi protetti possono contenere solo l'interfaccia pubblica; nessuna protezione viene rimossa.");
            Snapshot.Limitations.Add("HMI, allarmi e parametri azionamenti richiedono adapter dedicati; l'inventario hardware non ne sostituisce l'esportazione.");
        }
        public void CheckCancellation() { cancellation.ThrowIfCancellationRequested(); }
        public void Issue(string source, Exception error)
        {
            if (error is OperationCanceledException) throw error;
            Snapshot.Issues.Add(new ExportIssue { Source = source, Message = error.GetType().Name + ": " + error.Message });
        }
        public void Write(string project, string plc, string group, string name, string kind, string language,
            bool isProtected, Action<FileInfo> export)
        {
            CheckCancellation();
            var id = string.Join("\n", new[] { project, plc, group, kind, name });
            var artifact = new ExportArtifact { Id = ExportPaths.Hash(id), Project = project, Plc = plc,
                Group = group, Name = name, Kind = kind, Language = language, Protected = isProtected, Status = "Failed" };
            artifact.RelativePath = "plc/" + ExportPaths.Segment(project) + "/" + ExportPaths.Segment(plc) + "/" +
                ExportPaths.Segment(group + "/" + kind) + "/" + ExportPaths.Segment(name) + ".xml";
            var path = ExportPaths.Resolve(directory, artifact.RelativePath);
            if (Snapshot.Artifacts.Exists(a => a.Id == artifact.Id)) throw new IOException("Identità duplicata nell'esportazione: " + name);
            Snapshot.Artifacts.Add(artifact);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                // Siemens receives a unique path. Existing files are never overwritten.
                if (File.Exists(path)) throw new IOException("Il file di destinazione esiste già.");
                export(new FileInfo(path));
                if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("Nessun XML prodotto dall'adapter.");
                artifact.Status = isProtected ? "InterfaceOnly" : "Exported";
                artifact.Bytes = new FileInfo(path).Length;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Issue(project + "/" + plc + "/" + group + "/" + name, ex);
                // No failed file is listed as usable by the package writer.
                artifact.RelativePath = null;
                if (File.Exists(path)) File.Delete(path);
            }
            progress?.Report(new ExportProgress { Completed = Snapshot.Artifacts.Count, Message = name + " · " + artifact.Status });
            CheckCancellation();
        }
    }
}
