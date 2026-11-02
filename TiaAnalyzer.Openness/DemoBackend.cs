using System;
using System.Collections.Generic;
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
        private sealed class DemoSession : IAttachedPortal
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
        }
    }
}
