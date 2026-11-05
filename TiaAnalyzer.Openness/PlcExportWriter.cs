using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    internal sealed class PlcExportWriter
    {
        private readonly string directory;
        private readonly IProgress<ExportProgress> progress;
        private readonly CancellationToken cancellation;
        private readonly ContentFileCache cache;
        private readonly ExportOptions options;
        private readonly HashSet<string> ids = new HashSet<string>(), notes = new HashSet<string>();
        private readonly List<Tuple<ExportArtifact, Action>> plan = new List<Tuple<ExportArtifact, Action>>();
        private readonly ExportCheckpoint checkpoint;
        private readonly Dictionary<string, CheckpointEntry> previous = new Dictionary<string, CheckpointEntry>();
        public bool CaptureCrossReferences { get; }
        public bool CaptureFingerprints { get { return options.Mode != ExportMode.Quick; } }
        public ExportRun Run { get { return options.Run; } }
        public string SourceIdentity { get; set; } = "";
        public string SourceProof { get; set; }
        private string Proof { get { return Snapshot.IsDemo ? SourceIdentity : SourceProof; } }
        public string ApiIdentity { get; set; } = "";
        public int CrossReferenceVisits { get; set; }
        public ExportSnapshot Snapshot { get; }
        public PlcExportWriter(string directory, bool demo, IProgress<ExportProgress> progress, CancellationToken cancellation, ExportOptions options = null)
        {
            this.directory = Path.GetFullPath(directory); this.progress = progress; this.cancellation = cancellation;
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Any(p => Path.GetFileName(p) != "operations.jsonl" || options?.Run == null || options.Run.DirectoryPath != this.directory))
                throw new IOException("Usare una cartella di esportazione nuova e vuota.");
            Directory.CreateDirectory(directory);
            this.options = options ?? new ExportOptions();
            if (!Enum.IsDefined(typeof(ExportMode), this.options.Mode)) throw new ArgumentOutOfRangeException(nameof(options));
            Snapshot = new ExportSnapshot { IsDemo = demo, AcquisitionState = "Incomplete", ExportMode = this.options.Mode.ToString(), ExportId = Run?.Id ?? Guid.NewGuid().ToString("N") };
            checkpoint = new ExportCheckpoint { ExportId = Snapshot.ExportId };
            CaptureCrossReferences = this.options.CaptureCrossReferences && this.options.Mode != ExportMode.Quick;
            if (this.options.ReuseUnchangedXml && !string.IsNullOrWhiteSpace(this.options.CacheDirectory))
                cache = new ContentFileCache(Path.Combine(this.options.CacheDirectory, "xml-v2"));
            if (!string.IsNullOrWhiteSpace(this.options.ResumeDirectory))
            {
                try { previous = CheckpointStore.Read(this.options.ResumeDirectory).Entries.ToDictionary(e => e.Artifact.Id, StringComparer.Ordinal); }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException || ex is System.Runtime.Serialization.SerializationException)
                { Note("Checkpoint ignorato: " + ex.Message); }
            }
            Snapshot.Limitations.Add("Snapshot offline sequenziale: non contiene valori runtime e non è una transazione atomica del progetto.");
            Snapshot.Limitations.Add("I blocchi protetti possono contenere solo l'interfaccia pubblica; nessuna protezione viene rimossa.");
            Snapshot.Limitations.Add("HMI, allarmi e parametri azionamenti richiedono adapter dedicati.");
            if (this.options.Mode == ExportMode.Quick) Snapshot.Metrics.SkippedPhases.AddRange(new[] { "Fingerprint", "CrossReferences", "LogicAnalysis" });
            if (this.options.Mode == ExportMode.Quick) Snapshot.Limitations.Add("RAPIDA: cross-reference, fingerprint e analisi logica opzionale omessi; copertura parziale.");
            SaveCheckpoint("Incomplete");
        }
        public T Call<T>(string operation, string phase, string name, string kind, string path, Func<T> action)
        {
            CheckCancellation();
            return Run == null ? action() : Run.Call(operation, phase, name, kind, path, action);
        }
        public void Call(string operation, string phase, string name, string kind, string path, Action action)
        { Call(operation, phase, name, kind, path, () => { action(); return true; }); }
        public IEnumerable<T> Enumerate<T>(string operation, string path, Func<IEnumerable<T>> source, string phase = "Enumeration")
        {
            if (phase == "CrossReferences" && CrossReferenceVisits >= 200000) yield break;
            var collection = Call(operation + ".Collection", phase, "", "Collection", path, source);
            var enumerator = Call(operation + ".GetEnumerator", phase, "", "Collection", path, () => collection.GetEnumerator());
            try
            {
                while ((phase != "CrossReferences" || CrossReferenceVisits < 200000) &&
                    Call(operation + ".MoveNext", phase, "", "Collection", path, () => enumerator.MoveNext()))
                {
                    var current = Call(operation + ".Current", phase, "", "Collection", path, () => enumerator.Current);
                    if (phase == "CrossReferences") CrossReferenceVisits++;
                    yield return current;
                }
            }
            finally { if (Run == null) enumerator.Dispose(); else Run.Call(operation + ".Dispose", phase, "", "Collection", path, () => enumerator.Dispose()); }
        }
        public void Plan(string project, string plc, string group, string name, string kind, Action action)
        { if (plan.Count >= 100000) throw new InvalidOperationException("Limite di 100000 oggetti nell'inventario superato.");
            plan.Add(Tuple.Create(new ExportArtifact { Id = ExportPaths.Identity(project, plc, group, kind, name), Project = project, Plc = plc, Group = group, Name = name, Kind = kind }, action)); }
        public ExportSnapshot ExecutePlan()
        {
            var targets = new HashSet<string>((options.TargetObjects ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
            if (options.Mode == ExportMode.Targeted && targets.Count == 0) throw new InvalidOperationException("La modalità mirata richiede nomi o ID espliciti.");
            var selected = plan.Where(p => options.Mode != ExportMode.Targeted || targets.Contains(p.Item1.Id) || targets.Contains(p.Item1.Name)).ToList();
            if (options.Mode == ExportMode.Targeted && targets.Any(t => !selected.Any(p => p.Item1.Id == t || p.Item1.Name == t)))
                throw new InvalidOperationException("Uno o più oggetti mirati non esistono nell'inventario corrente.");
            if (plan.Count == 0) Snapshot.Issues.Add(new ExportIssue { Source = "PLC", Message = "Nessun oggetto PLC esportabile trovato." });
            Run?.Inventory(selected.Count);
            Snapshot.Inventory = plan.Select(p => p.Item1).ToList();
            if (options.Mode == ExportMode.Targeted)
                Snapshot.Selection = new PackageSelection { Query = string.Join(", ", targets), Mode = "ExplicitObjects", TotalArtifacts = plan.Count,
                    IncludedArtifacts = selected.Count, ExcludedIds = plan.Except(selected).Select(p => p.Item1.Id).ToList() };
            foreach (var item in selected) { CheckCancellation(); item.Item2(); }
            var state = Snapshot.Issues.Count > 0 ? "Incomplete" : Snapshot.Artifacts.Any(a => a.CrossReferencesStatus == "Failed" || a.CrossReferencesStatus == "Partial") ? "Partial" : options.Mode == ExportMode.Quick || options.Mode == ExportMode.Targeted ? "Partial" : "Collected";
            SaveCheckpoint(state); Snapshot.AcquisitionState = state; return Snapshot;
        }
        public void SaveCheckpoint(string state)
        {
            checkpoint.State = state;
            try { CheckpointStore.Write(Path.Combine(directory, "checkpoint.json"), new ExportCheckpoint { ExportId = checkpoint.ExportId, State = state, EntryDirectory = "checkpoint-entries" }); }
            catch (Exception ex) { throw new ExportDiagnosticsException("Checkpoint non salvato: interrompere fra due chiamate.", ex); }
            Run?.Checkpoint(Path.Combine(directory, "checkpoint.json"));
        }
        public void CheckCancellation() { cancellation.ThrowIfCancellationRequested(); }
        public void Note(string message) { if (notes.Add(message)) Snapshot.Notes.Add(message); }
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
            var staging = Path.Combine(directory, ".pending_" + Guid.NewGuid().ToString("N") + ".partial");
            var temporary = Path.Combine(staging, artifact.Id + ".xml");
            if (!ids.Add(artifact.Id)) throw new IOException("Identità duplicata: " + name);
            Snapshot.Artifacts.Add(artifact);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (Directory.Exists(staging)) throw new IOException("Directory temporanea già esistente.");
                Directory.CreateDirectory(staging);
                if (File.Exists(path)) throw new IOException("Il file di destinazione esiste già.");
                var before = CaptureFingerprints ? Call("Fingerprint.Before", "Fingerprint", name, kind, group, () => readStamp?.Invoke()) : null;
                var key = before == null || !before.ReliableForXml || string.IsNullOrWhiteSpace(before.Token) || string.IsNullOrWhiteSpace(ApiIdentity) || string.IsNullOrWhiteSpace(SourceIdentity) || string.IsNullOrWhiteSpace(Proof) ? null :
                    ExportPaths.Identity("xml-v2-WithDefaults", ApiIdentity, SourceIdentity, Proof, Snapshot.IsDemo.ToString(), artifact.Id, artifact.Language, artifact.Protected.ToString(), before.Token);
                CheckpointEntry entry; previous.TryGetValue(artifact.Id, out entry);
                string warning = null;
                if (entry != null && key != null && entry.Artifact.Project == artifact.Project && entry.Artifact.Plc == artifact.Plc &&
                    entry.Artifact.Group == artifact.Group && entry.Artifact.Name == artifact.Name && entry.Artifact.Kind == artifact.Kind &&
                    entry.Artifact.Language == artifact.Language && entry.Artifact.Protected == artifact.Protected)
                {
                    try { artifact.ReusedXml = Call("Checkpoint.VerifyAndRestore", "LocalCache", name, kind, group, () => CheckpointStore.Restore(options.ResumeDirectory, entry, temporary, SourceIdentity, Proof, ApiIdentity, before.Token, cancellation)); }
                    catch (Exception ex) when (!(ex is ExportDiagnosticsException) && (ex is IOException || ex is System.Xml.XmlException || ex is UnauthorizedAccessException))
                    { Note(name + ": ripresa rifiutata: " + ex.Message); if (File.Exists(temporary)) File.Delete(temporary); }
                }
                if (!artifact.ReusedXml && cache != null && key != null) artifact.ReusedXml = Call("Cache.Restore", "LocalCache", name, kind, group, () => cache.Restore(key, temporary, cancellation, out warning));
                if (warning != null) { Snapshot.Metrics.CacheWarnings++; Note(warning); }
                if (!artifact.ReusedXml) Call("Export.XML", "XmlExport", name, kind, project + "/" + plc + "/" + group + "/" + name, () => export(new FileInfo(temporary)));
                var after = CaptureFingerprints ? Call("Fingerprint.After", "Fingerprint", name, kind, group, () => readStamp?.Invoke()) : null;
                if (artifact.ReusedXml && (after == null || !after.ReliableForXml || before.Token != after.Token))
                {
                    File.Delete(temporary); artifact.ReusedXml = false; before = after; key = null;
                    Call("Export.XML.Retry", "XmlExport", name, kind, group, () => export(new FileInfo(temporary)));
                    after = CaptureFingerprints ? Call("Fingerprint.AfterRetry", "Fingerprint", name, kind, group, () => readStamp?.Invoke()) : null;
                    Note(name + ": impronta cambiata durante il riuso; riesportato.");
                }
                if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0) throw new IOException("Nessun XML prodotto.");
                Call("Validate.XML", "XmlParsing", name, kind, group, () => CheckpointStore.ValidateXml(temporary, cancellation));
                artifact.Sha256 = CheckpointStore.FileHash(temporary, cancellation);
                artifact.Bytes = new FileInfo(temporary).Length;
                using (var flush = new FileStream(temporary, FileMode.Open, FileAccess.Write)) flush.Flush(true);
                CheckCancellation(); File.Move(temporary, path);
                artifact.Status = isProtected ? "InterfaceOnly" : "Exported";
                artifact.Fingerprints = before != null && after != null && before.Token == after.Token ? after.Fingerprints : null;
                var completedEntry = new CheckpointEntry { Artifact = artifact, SourceIdentity = SourceIdentity, SourceProof = Proof, ApiIdentity = ApiIdentity,
                    Stamp = before != null && after != null && before.ReliableForXml && after.ReliableForXml && before.Token == after.Token ? after.Token : null };
                try { CheckpointStore.PutEntry(directory, completedEntry); }
                catch (Exception ex) { throw new ExportDiagnosticsException("Registrazione checkpoint XML fallita.", ex); }
                SaveCheckpoint("Incomplete"); Run?.Completed(name);
                if (artifact.ReusedXml) Snapshot.Metrics.XmlReused++; else Snapshot.Metrics.XmlExported++;
                if (before != null && after != null && before.ReliableForXml && after.ReliableForXml && before.Token == after.Token && key != null && cache != null && !artifact.ReusedXml)
                    try { Call("Cache.Save", "LocalCache", name, kind, group, () => cache.Save(key, path, cancellation)); }
                    catch (Exception ex) when (!(ex is ExportDiagnosticsException) && (ex is IOException || ex is UnauthorizedAccessException))
                    { Snapshot.Metrics.CacheWarnings++; Note("Cache XML non aggiornata: " + ex.Message); }
                else if (before != null && (after == null || before.Token != after.Token))
                { Note(name + ": fingerprint instabile; XML non riusabile."); Issue(name, new InvalidOperationException("Fingerprint cambiato durante l'export: ripetere la raccolta con progetto stabile.")); }
            }
            catch (OperationCanceledException) { artifact.RelativePath = null; SaveCheckpoint("Cancelled"); throw; }
            catch (Exception ex) when (!(ex is ExportDiagnosticsException))
            {
                Issue(project + "/" + plc + "/" + group + "/" + name, ex); artifact.Status = "Failed"; artifact.RelativePath = null;
                if (File.Exists(path)) File.Delete(path);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            progress?.Report(new ExportProgress { Completed = Snapshot.Artifacts.Count, Message = name + " · " + artifact.Status + (artifact.ReusedXml ? " · cache" : "") });
            CheckCancellation(); return artifact;
        }
    }
}
