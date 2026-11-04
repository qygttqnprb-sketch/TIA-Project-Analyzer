#if SIEMENS_V21
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.SW;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    public sealed partial class SiemensBackend
    {
        private sealed partial class AttachedPortal
        {
            private static void ExportObject(PlcExportWriter writer, IEngineeringServiceProvider source, string project, string plc, string group,
                string name, string kind, string language, bool isProtected, Action<FileInfo> export, int? number = null)
            {
                var artifact = writer.Write(project, plc, group, name, kind, language, isProtected, export, number,
                    kind == "TagTable" ? (Func<ExportStamp>)null : () => ReadStamp(source, writer, name));
                artifact.CrossReferencesStatus = "NotRequested";
                if (writer.CaptureCrossReferences) ReadReferences(source, artifact, writer);
            }
            private static ExportStamp ReadStamp(IEngineeringServiceProvider source, PlcExportWriter writer, string name)
            {
                writer.CheckCancellation();
                try
                {
                    var provider = source.GetService<FingerprintProvider>();
                    if (provider == null) { writer.Note(name + ": fingerprint non disponibile, export XML completo."); return null; }
                    var fingerprints = provider.GetFingerprints().Select(f => new ObjectFingerprint { Kind = f.Id.ToString(), Value = f.Value })
                        .OrderBy(f => f.Kind, StringComparer.Ordinal).ToList();
                    if (fingerprints.Count == 0 || fingerprints.Any(f => string.IsNullOrEmpty(f.Value))) return null;
                    return new ExportStamp { Fingerprints = fingerprints,
                        Token = ExportPaths.Identity(fingerprints.SelectMany(f => new[] { f.Kind, f.Value }).ToArray()) };
                }
                catch (EngineeringSecurityException ex)
                { writer.Note(name + ": fingerprint non autorizzato; si tenta solo l'export XML consentito. " + ex.Message); return null; }
                catch (Exception ex) when (ex is EngineeringException || ex is NotSupportedException)
                { writer.Note(name + ": fingerprint non utilizzabile; export completo senza compilare. " + ex.Message); return null; }
            }
            private static void ReadReferences(IEngineeringServiceProvider source, ExportArtifact artifact, PlcExportWriter writer)
            {
                writer.CheckCancellation();
                try
                {
                    var service = source.GetService<CrossReferenceService>();
                    if (service == null) { artifact.CrossReferencesStatus = "Unavailable"; writer.Note(artifact.Name + ": servizio cross-reference non disponibile."); return; }
                    var result = service.GetCrossReferences(CrossReferenceFilter.AllObjects);
                    artifact.CrossReferencesStatus = "Available";
                    foreach (SourceObject item in result.Sources) ReadReferenceSource(item, artifact, writer, 0);
                }
                catch (EngineeringSecurityException ex)
                { artifact.CrossReferencesStatus = "Failed"; writer.Note(artifact.Name + ": cross-reference non autorizzati, nessuna autorizzazione modificata. " + ex.Message); }
                catch (Exception ex) when (ex is EngineeringException || ex is NotSupportedException)
                { artifact.CrossReferencesStatus = "Failed"; writer.Note(artifact.Name + ": cross-reference incompleti; gli XML restano disponibili. " + ex.Message); }
            }
            private static void ReadReferenceSource(SourceObject source, ExportArtifact artifact, PlcExportWriter writer, int depth)
            {
                writer.CheckCancellation();
                if (depth > 64 || ++writer.CrossReferenceVisits > 200000 || writer.Snapshot.CrossReferences.Count >= 200000)
                { artifact.CrossReferencesStatus = "Partial"; writer.Note("Cross-reference parziali: raggiunto il limite di profondità o di 200000 posizioni."); return; }
                foreach (ReferenceObject reference in source.References)
                    foreach (Location location in reference.Locations)
                    {
                        writer.CheckCancellation();
                        if (writer.Snapshot.CrossReferences.Count >= 200000)
                        { artifact.CrossReferencesStatus = "Partial"; writer.Note("Cross-reference parziali: raggiunto il limite di 200000 posizioni."); return; }
                        writer.Snapshot.CrossReferences.Add(new PlcCrossReference { QueriedArtifactId = artifact.Id,
                            Project = artifact.Project, Plc = artifact.Plc, SourceName = source.Name, SourceAddress = source.Address, SourcePath = source.Path,
                            ReferenceName = reference.Name, ReferencePath = reference.Path, ReferenceAddress = reference.Address, ReferenceDevice = reference.Device,
                            Location = location.ReferenceLocation, Access = location.Access.ToString(), ReferenceType = location.ReferenceType.ToString(), Provider = "Siemens V21" });
                    }
                foreach (SourceObject child in source.Children) ReadReferenceSource(child, artifact, writer, depth + 1);
            }
        }
    }
}
#endif
