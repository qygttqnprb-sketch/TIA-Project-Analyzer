using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    public sealed class LoadedSnapshot
    {
        public string Path { get; set; }
        public ExportSnapshot Snapshot { get; set; }
        public ProjectAnalysis Analysis { get; set; }
        public int VerifiedFiles { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }
    public sealed class SourcePreview
    {
        public string Text { get; set; }
        public bool Truncated { get; set; }
        public string File { get; set; }
        public string Network { get; set; }
    }
    public static class SnapshotReader
    {
        private const long MaximumTotalXmlBytes = 2L * 1024 * 1024 * 1024;
        public static LoadedSnapshot Open(string path, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            path = System.IO.Path.GetFullPath(path);
            if (!new[] { ".zip", ".json" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Selezionare uno ZIP o un manifest JSON.");
            using (var content = new SnapshotContent(path, MaximumTotalXmlBytes))
            {
                var snapshot = content.ReadManifest(cancellation);
                snapshot.CrossReferences = snapshot.CrossReferences ?? new List<PlcCrossReference>();
                snapshot.Components = snapshot.Components ?? new List<ProjectComponent>();
                snapshot.Limitations = snapshot.Limitations ?? new List<string>();
                snapshot.Notes = snapshot.Notes ?? new List<string>();
                var result = new LoadedSnapshot { Path = path, Snapshot = snapshot };
                var timer = System.Diagnostics.Stopwatch.StartNew();
                snapshot.Metrics = new ExportMetrics { ManagedBytesBefore = GC.GetTotalMemory(false) };
                result.Analysis = XmlProjectAnalyzer.AnalyzeVerified(snapshot, artifact =>
                {
                    var document = content.LoadXml(artifact, cancellation); result.VerifiedFiles++; return document;
                }, cancellation);
                snapshot.Metrics.AnalysisMilliseconds = timer.ElapsedMilliseconds;
                snapshot.Metrics.ManagedBytesAfter = GC.GetTotalMemory(false);
                if (snapshot.AcquisitionState != null && snapshot.AcquisitionState != "Complete") result.Warnings.Add("Stato raccolta: " + snapshot.AcquisitionState + "; nessuna completezza globale dichiarata.");
                var failed = snapshot.Artifacts.Count(a => a.Status == "Failed");
                var interfaces = snapshot.Artifacts.Count(a => a.Status == "InterfaceOnly");
                if (failed > 0) result.Warnings.Add(failed + " oggetti dichiarati Failed: contenuto non disponibile.");
                if (interfaces > 0) result.Warnings.Add(interfaces + " oggetti con sola interfaccia: logica interna non disponibile.");
                if (snapshot.Selection != null) result.Warnings.Add("Pacchetto mirato: " + snapshot.Selection.IncludedArtifacts + " di " +
                    snapshot.Selection.TotalArtifacts + " oggetti. Gli esclusi non vengono ricostruiti.");
                result.Warnings.AddRange(snapshot.Limitations.Where(s => s != null));
                result.Warnings.AddRange(snapshot.Notes.Where(s => s != null));
                result.Warnings.AddRange(snapshot.Issues.Where(i => i != null).Select(i => i.Source + ": " + i.Message));
                result.Analysis.Warnings.AddRange(result.Warnings);
                cancellation.ThrowIfCancellationRequested(); return result;
            }
        }
        public static SourcePreview Preview(LoadedSnapshot loaded, string artifactId, string networkId, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var artifact = loaded.Snapshot.Artifacts.SingleOrDefault(a => a.Id == artifactId && a.Status != "Failed" && a.RelativePath != null);
            if (artifact == null) throw new InvalidDataException("Oggetto non incluso nel contesto caricato.");
            using (var content = new SnapshotContent(loaded.Path, MaximumTotalXmlBytes))
            {
                var document = content.LoadXml(artifact, cancellation);
                var units = document.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit" && (string)e.Attribute("ID") == networkId).ToList();
                if (units.Count != 1) throw new InvalidDataException("Rete assente o non identificata in modo univoco.");
                var source = units[0].Descendants().FirstOrDefault(e => e.Name.LocalName == "NetworkSource");
                if (source == null) throw new InvalidDataException("Sorgente della rete non disponibile.");
                var text = source.ToString(SaveOptions.DisableFormatting);
                var result = new SourcePreview { File = artifact.RelativePath, Network = networkId, Truncated = text.Length > 16000 };
                result.Text = result.Truncated ? text.Substring(0, 16000) : text;
                cancellation.ThrowIfCancellationRequested(); return result;
            }
        }
    }
}
