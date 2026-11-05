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
                var identity = writer.SourceIdentity; var proof = writer.SourceProof;
                writer.Plan(project, plc, group, name, kind, () =>
                {
                    writer.SourceIdentity = identity; writer.SourceProof = proof;
                var artifact = writer.Write(project, plc, group, name, kind, language, isProtected, export, number,
                    kind == "TagTable" ? (Func<ExportStamp>)null : () => ReadStamp(source, writer, name, project + "/" + plc + "/" + group + "/" + name));
                artifact.CrossReferencesStatus = "NotRequested";
                if (writer.CaptureCrossReferences && artifact.Status != "Failed")
                    writer.Call("CrossReference.Read", "CrossReferences", name, kind, group, () => ReadReferences(source, artifact, writer));
                });
            }
            private static ExportStamp ReadStamp(IEngineeringServiceProvider source, PlcExportWriter writer, string name, string path = "")
            {
                writer.CheckCancellation();
                try
                {
                    var provider = writer.Call("GetService<FingerprintProvider>", "Fingerprint", name, "Object", path, () => source.GetService<FingerprintProvider>());
                    if (provider == null) { writer.Note(name + ": fingerprint non disponibile, export XML completo."); return null; }
                    var fingerprints = writer.Enumerate("FingerprintProvider.GetFingerprints", name, () => provider.GetFingerprints(), "Fingerprint").Select(f =>
                        writer.Call("Fingerprint.Properties", "Fingerprint", name, "Object", path, () => new ObjectFingerprint { Kind = writer.Call("Fingerprint.Id", "Fingerprint", name, "Object", path, () => f.Id.ToString()), Value = writer.Call("Fingerprint.Value", "Fingerprint", name, "Object", path, () => f.Value) }))
                        .OrderBy(f => f.Kind, StringComparer.Ordinal).ToList();
                    if (fingerprints.Count == 0 || fingerprints.Any(f => string.IsNullOrEmpty(f.Value))) return null;
                    writer.Note(name + ": fingerprint raccolto; copertura dell'intero XML non attestata su V21, riuso automatico disabilitato.");
                    return new ExportStamp { ReliableForXml = false, Fingerprints = fingerprints,
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
                if (writer.CrossReferenceVisits >= 200000 || writer.Snapshot.CrossReferences.Count >= 200000)
                { artifact.CrossReferencesStatus = "Partial"; writer.Note("Budget globale cross-reference esaurito: nessuna ulteriore query AllObjects."); return; }
                try
                {
                    var service = writer.Call("GetService<CrossReferenceService>", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => source.GetService<CrossReferenceService>());
                    if (service == null) { artifact.CrossReferencesStatus = "Unavailable"; writer.Note(artifact.Name + ": servizio cross-reference non disponibile."); return; }
                    var result = writer.Call("GetCrossReferences(AllObjects)", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => service.GetCrossReferences(CrossReferenceFilter.AllObjects));
                    artifact.CrossReferencesStatus = "Available";
                    foreach (SourceObject item in writer.Enumerate("CrossReferences.Sources", artifact.Name, () => result.Sources, "CrossReferences"))
                    { ReadReferenceSource(item, artifact, writer, 0); if (writer.CrossReferenceVisits >= 200000 || writer.Snapshot.CrossReferences.Count >= 200000) { artifact.CrossReferencesStatus = "Partial"; break; } }
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
                foreach (ReferenceObject reference in writer.Enumerate("Source.References", artifact.Name, () => source.References, "CrossReferences"))
                    foreach (Location location in writer.Enumerate("Reference.Locations", artifact.Name, () => reference.Locations, "CrossReferences"))
                    {
                        writer.CheckCancellation();
                        if (writer.Snapshot.CrossReferences.Count >= 200000)
                        { artifact.CrossReferencesStatus = "Partial"; writer.Note("Cross-reference parziali: raggiunto il limite di 200000 posizioni."); return; }
                        writer.Snapshot.CrossReferences.Add(writer.Call("CrossReference.Properties", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => new PlcCrossReference { QueriedArtifactId = artifact.Id,
                            Project = artifact.Project, Plc = artifact.Plc, SourceName = writer.Call("source.Name", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => source.Name), SourceAddress = writer.Call("source.Address", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => source.Address), SourcePath = writer.Call("source.Path", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => source.Path),
                            ReferenceName = writer.Call("reference.Name", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => reference.Name), ReferencePath = writer.Call("reference.Path", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => reference.Path), ReferenceAddress = writer.Call("reference.Address", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => reference.Address), ReferenceDevice = writer.Call("reference.Device", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => reference.Device),
                            Location = writer.Call("location.ReferenceLocation", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => location.ReferenceLocation), Access = writer.Call("location.Access", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => location.Access.ToString()), ReferenceType = writer.Call("location.ReferenceType", "CrossReferences", artifact.Name, artifact.Kind, artifact.Group, () => location.ReferenceType.ToString()), Provider = "Siemens V21" }));
                    }
                foreach (SourceObject child in writer.Enumerate("Source.Children", artifact.Name, () => source.Children, "CrossReferences"))
                { ReadReferenceSource(child, artifact, writer, depth + 1); if (writer.CrossReferenceVisits >= 200000 || writer.Snapshot.CrossReferences.Count >= 200000) { artifact.CrossReferencesStatus = "Partial"; break; } }
            }
        }
    }
}
#endif
