using System.Collections.Generic;

namespace TiaAnalyzer.Core
{
    public sealed class EnvironmentCheck
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
    }

    public sealed class EnvironmentReport
    {
        // Library readiness only: never implies permission to connect to TIA.
        public bool Available { get; set; }
        public IList<EnvironmentCheck> Checks { get; set; }
    }

    public interface IEnvironmentProbe { EnvironmentReport Inspect(); }

    public sealed class ProjectComponent
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
    }

    public interface IReadOnlyTiaSession
    {
        bool IsConnected { get; }
        string Description { get; }
    }

    // No project-open, compile, download, write or save operations are exposed.
    public interface ITiaConnectionService
    {
        bool CanConnect { get; }
        IReadOnlyTiaSession Connect();
        IReadOnlyList<ProjectComponent> GetComponents(IReadOnlyTiaSession session);
    }
}
