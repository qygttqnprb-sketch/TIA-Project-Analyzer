using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

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
        public bool Available { get; set; }
        public IList<EnvironmentCheck> Checks { get; set; }
    }
    public interface IEnvironmentProbe { EnvironmentReport Inspect(); }
    public sealed class ProjectComponent
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public string Project { get; set; }
        public string Path { get; set; }
    }
    public sealed class TiaInstance
    {
        public int Id { get; set; }
        public string Version { get; set; }
        public string ExecutablePath { get; set; }
        public string ProjectPath { get; set; }
        public string DisplayName { get { return "PID " + Id + " · " + Version + " · " + (ProjectPath ?? "Progetto non disponibile"); } }
    }
    public enum ConnectionFailure { ApiUnavailable, InstanceUnavailable, AuthorizationDenied, ReadFailed, Unexpected }
    public sealed class TiaConnectionException : Exception
    {
        public ConnectionFailure Failure { get; private set; }
        public TiaConnectionException(ConnectionFailure failure, string message, Exception inner = null) : base(message, inner) { Failure = failure; }
    }
    // No write, save, compile, online access or project-open operations are exposed.
    public interface ITiaConnectionService
    {
        bool IsAvailable { get; }
        bool HasSession { get; }
        Task<IReadOnlyList<TiaInstance>> DiscoverAsync();
        Task<IReadOnlyList<ProjectComponent>> ConnectAsync(TiaInstance instance);
        Task<ExportSnapshot> ExportPlcAsync(string directory, IProgress<ExportProgress> progress, CancellationToken cancellation);
        Task DisconnectAsync();
        Task ShutdownAsync();
    }
    // All implementations are called serially on one adapter-owned worker.
    public interface ITiaBackend
    {
        bool IsAvailable { get; }
        IReadOnlyList<TiaInstance> Discover();
        IAttachedPortal Attach(TiaInstance instance);
    }
    public interface IAttachedPortal : IDisposable
    {
        IReadOnlyList<ProjectComponent> ReadComponents();
    }
}
