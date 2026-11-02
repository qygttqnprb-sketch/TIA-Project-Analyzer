using System;
using System.Collections.Generic;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    public sealed class SiemensConnectionService : ITiaConnectionService
    {
        public bool CanConnect { get { return false; } }
        public IReadOnlyTiaSession Connect()
        {
            throw new NotSupportedException("Connessione Siemens non implementata in V0.1. Integrazione da verificare su Windows.");
        }
        public IReadOnlyList<ProjectComponent> GetComponents(IReadOnlyTiaSession session)
        {
            throw new NotSupportedException("Analisi reale non disponibile in V0.1.");
        }
    }

    public sealed class DemoConnectionService : ITiaConnectionService
    {
        public bool CanConnect { get { return true; } }
        public IReadOnlyTiaSession Connect() { return new MockTiaSession(); }
        public IReadOnlyList<ProjectComponent> GetComponents(IReadOnlyTiaSession session)
        {
            if (!(session is MockTiaSession) || !session.IsConnected)
                throw new InvalidOperationException("È richiesta una sessione dimostrativa.");
            return new List<ProjectComponent>
            {
                new ProjectComponent { Name = "Linea confezionamento", Kind = "Progetto sintetico", Status = "DEMO" },
                new ProjectComponent { Name = "PLC_Conveyor / CPU 1511-1 PN", Kind = "Controllore sintetico", Status = "DEMO" },
                new ProjectComponent { Name = "Main [OB1]", Kind = "Blocco sintetico", Status = "DEMO" },
                new ProjectComponent { Name = "ConveyorControl [FB10]", Kind = "Blocco sintetico", Status = "DEMO" },
                new ProjectComponent { Name = "ConveyorData [DB10]", Kind = "Dati sintetici", Status = "DEMO" },
                new ProjectComponent { Name = "HMI_Packaging", Kind = "Pannello sintetico", Status = "DEMO" }
            }.AsReadOnly();
        }
    }
}
