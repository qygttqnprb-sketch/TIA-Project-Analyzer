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
        private readonly ContentFileCache cache;
        private readonly System.Collections.Generic.HashSet<string> ids = new System.Collections.Generic.HashSet<string>();
        private readonly System.Collections.Generic.HashSet<string> notes = new System.Collections.Generic.HashSet<string>();
        public bool CaptureCrossReferences { get; }
        public string SourceIdentity { get; set; } = "";
        public string ApiIdentity { get; set; } = "";
        public int CrossReferenceVisits { get; set; }
        public ExportSnapshot Snapshot { get; }
        public PlcExportWriter(string directory, bool demo, IProgress<ExportProgress> progress, CancellationToken cancellation, ExportOptions options = null)
        {
            this.directory = Path.GetFullPath(directory); this.progress = progress; this.cancellation = cancellation;
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length != 0)
                throw new IOException("Usare una cartella di esportazione nuova e vuota.");
            Directory.CreateDirectory(directory);
            Snapshot = new ExportSnapshot { IsDemo = demo };
            options = options ?? new ExportOptions(); CaptureCrossReferences = options.CaptureCrossReferences;
            if (options.ReuseUnchangedXml && !string.IsNullOrWhiteSpace(options.CacheDirectory))
                cache = new ContentFileCache(Path.Combine(options.CacheDirectory, "xml-v1"));
            Snapshot.Limitations.Add("Snapshot offline sequenziale: non contiene valori runtime e non è una transazione atomica del progetto.");
            Snapshot.Limitations.Add("I blocchi protetti possono contenere solo l'interfaccia pubblica; nessuna protezione viene rimossa.");
            Snapshot.Limitations.Add("HMI, allarmi e parametri azionamenti richiedono adapter dedicati; l'inventario hardware non ne sostituisce l'esportazione.");
        }
        public void CheckCancellation() { cancellation.ThrowIfCancellationRequested(); }
        public void Note(string message)
        {
            if (notes.Add(message)) Snapshot.Notes.Add(message);
        }
        public void Issue(string source, Exception error)
        {
            if (error is OperationCanceledException) throw error;
            Snapshot.Issues.Add(new ExportIssue { Source = source, Message = error.GetType().Name + ": " + error.Message });
        }
        public ExportArtifact Write(string project, string plc, string group, string name, string kind, string language,
            bool isProtected, Action<FileInfo> export, int? number = null, Func<ExportStamp> readStamp = null)
        {
            CheckCancellation();
            var artifact = new ExportArtifact { Id = ExportPaths.Identity(project, plc, group, kind, name), Project = project, Plc = plc,
                Group = group, Name = name, Kind = kind, Language = language, Number = number, Protected = isProtected, Status = "Failed" };
            artifact.RelativePath = "plc/" + ExportPaths.Segment(project) + "/" + ExportPaths.Segment(plc) + "/" +
                ExportPaths.Segment(group + "/" + kind) + "/" + ExportPaths.Segment(name) + ".xml";
            var path = ExportPaths.Resolve(directory, artifact.RelativePath);
            if (!ids.Add(artifact.Id)) throw new IOException("Identità duplicata nell'esportazione: " + name);
            Snapshot.Artifacts.Add(artifact);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Siemens receives a unique path. Existing files are never overwritten.
                if (File.Exists(path)) throw new IOException("Il file di destinazione esiste già.");
                var before = readStamp?.Invoke();
                var key = before == null || string.IsNullOrWhiteSpace(before.Token) || string.IsNullOrWhiteSpace(ApiIdentity) || string.IsNullOrWhiteSpace(SourceIdentity) ? null :
                    ExportPaths.Identity("xml-v1-WithDefaults", ApiIdentity, SourceIdentity, Snapshot.IsDemo.ToString(), artifact.Id,
                        artifact.Language, artifact.Protected.ToString(), before.Token);
                string warning = null;
                artifact.ReusedXml = cache != null && key != null && cache.Restore(key, path, cancellation, out warning);
                if (warning != null) { Snapshot.Metrics.CacheWarnings++; Note(warning); }
                if (!artifact.ReusedXml) export(new FileInfo(path));
                var after = readStamp?.Invoke();
                if (artifact.ReusedXml && (after == null || before.Token != after.Token))
                {
                    File.Delete(path); artifact.ReusedXml = false; before = after; key = null;
                    export(new FileInfo(path)); after = readStamp?.Invoke();
                    Note(name + ": impronta cambiata durante il riuso; eseguito export completo.");
                }
                if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("Nessun XML prodotto dall'adapter.");
                artifact.Status = isProtected ? "InterfaceOnly" : "Exported";
                artifact.Bytes = new FileInfo(path).Length;
                artifact.Fingerprints = before != null && after != null && before.Token == after.Token ? after.Fingerprints : null;
                if (artifact.ReusedXml) Snapshot.Metrics.XmlReused++; else Snapshot.Metrics.XmlExported++;
                if (before != null && after != null && before.Token == after.Token && key != null && cache != null && !artifact.ReusedXml)
                {
                    try { cache.Save(key, path, cancellation); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    { Snapshot.Metrics.CacheWarnings++; Note("Cache XML non aggiornata: " + ex.Message); }
                }
                else if (before != null && (after == null || before.Token != after.Token))
                    Note(name + ": stato modificato durante l'export, oggetto non inserito in cache; ripetere la raccolta senza modifiche contemporanee.");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Issue(project + "/" + plc + "/" + group + "/" + name, ex);
                // No failed file is listed as usable by the package writer.
                artifact.RelativePath = null;
                if (File.Exists(path)) File.Delete(path);
            }
            progress?.Report(new ExportProgress { Completed = Snapshot.Artifacts.Count, Message = name + " · " + artifact.Status + (artifact.ReusedXml ? " · cache" : "") });
            CheckCancellation();
            return artifact;
        }
    }
}
