using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    public sealed class DemoBackend : ITiaBackend
    {
        public bool IsAvailable { get { return true; } }
        public IReadOnlyList<TiaInstance> Discover()
        {
            return new[] { new TiaInstance { Id = 21001, Version = "DEMO V21", ExecutablePath = "Sintetico / nessun eseguibile", ProjectPath = "Linea confezionamento (sintetico)" } };
        }
        public IAttachedPortal Attach(TiaInstance instance) { return new DemoSession(); }
        private sealed class DemoSession : IAttachedPortal, IPlcExportSession
        {
            private bool disposed;
            public IReadOnlyList<ProjectComponent> ReadComponents()
            {
                if (disposed) throw new ObjectDisposedException(nameof(DemoSession));
                var items = new List<ProjectComponent>();
                foreach (var name in new[] { "Linea confezionamento", "PLC_Conveyor / CPU 1511-1 PN", "Main [OB1]", "ConveyorControl [FB10]", "ConveyorData [DB10]", "HMI_Packaging" })
                    items.Add(new ProjectComponent { Name = name, Kind = "Componente sintetico", Status = "DEMO", Project = "Linea confezionamento", Path = "DEMO/" + name });
                return items.AsReadOnly();
            }
            public void Dispose() { disposed = true; }
            public ExportSnapshot ExportPlc(string directory, IProgress<ExportProgress> progress, CancellationToken cancellation, ExportOptions options = null)
            {
                if (disposed) throw new ObjectDisposedException(nameof(DemoSession));
                var writer = new PlcExportWriter(directory, true, progress, cancellation, options) { SourceIdentity = "synthetic-conveyor-v1", ApiIdentity = "DEMO-XML-v1" };
                writer.Snapshot.Components.AddRange(ReadComponents());
                foreach (var document in DemoDocuments.Create())
                {
                    var artifact = writer.Write("DEMO Linea confezionamento", "PLC_Conveyor", document.Group, document.Name, document.Kind,
                        document.Language, false, file => File.WriteAllText(file.FullName, document.Xml, new System.Text.UTF8Encoding(false)),
                        readStamp: document.Kind == "TagTable" ? (Func<ExportStamp>)null : () => new ExportStamp
                        { Token = ExportPaths.Hash(document.Xml), Fingerprints = new List<ObjectFingerprint> { new ObjectFingerprint { Kind = "DemoXml", Value = ExportPaths.Hash(document.Xml) } } });
                    artifact.CrossReferencesStatus = writer.CaptureCrossReferences ? "Demo" : "NotRequested";
                    if (writer.CaptureCrossReferences && document.Name == "ConveyorControl")
                        foreach (var name in new[] { "SafetyOK", "DriveReady", "JamDetected", "MotorRun" })
                            writer.Snapshot.CrossReferences.Add(new PlcCrossReference { QueriedArtifactId = artifact.Id, Project = artifact.Project, Plc = artifact.Plc,
                                SourceName = name, ReferenceName = artifact.Name, Location = "Rete 1", Access = name == "MotorRun" ? "Write" : "Read", Provider = "DEMO" });
                }
                return writer.Snapshot;
            }
        }
    }
}
