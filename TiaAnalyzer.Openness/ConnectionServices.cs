using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    // Owns only the handle returned by Attach, never a newly started TIA process.
    public sealed class TiaConnectionService : ITiaConnectionService
    {
        private readonly ITiaBackend backend;
        private readonly SerialWorker worker = new SerialWorker();
        private IAttachedPortal attached;
        private IReadOnlyList<TiaInstance> discovered = new List<TiaInstance>();
        public TiaConnectionService(ITiaBackend backend) { this.backend = backend; }
        public bool HasSession { get { return attached != null; } }
        public bool IsAvailable { get { return backend.IsAvailable; } }
        public Task<IReadOnlyList<TiaInstance>> DiscoverAsync()
        {
            return worker.Run(() =>
            {
                discovered = new List<TiaInstance>();
                discovered = backend.Discover();
                return discovered;
            });
        }
        public Task<IReadOnlyList<ProjectComponent>> ConnectAsync(TiaInstance instance)
        {
            return worker.Run(() =>
            {
                if (attached != null) throw new InvalidOperationException("Disconnettere la sessione corrente prima di collegarsi.");
                TiaInstance selected = null;
                foreach (var candidate in discovered)
                    if (instance != null && ReferenceEquals(candidate, instance)) selected = candidate;
                if (selected == null) throw new TiaConnectionException(ConnectionFailure.InstanceUnavailable, "Selezionare un'istanza dall'ultimo rilevamento.");
                IAttachedPortal pending = backend.Attach(selected);
                if (pending == null) throw new TiaConnectionException(ConnectionFailure.Unexpected, "Attach non ha restituito una sessione.");
                try
                {
                    var components = pending.ReadComponents();
                    attached = pending;
                    return components;
                }
                catch (Exception readError)
                {
                    try { pending.Dispose(); }
                    catch (Exception releaseError) { attached = pending; throw new AggregateException("Lettura e rilascio della sessione non riusciti.", readError, releaseError); }
                    throw;
                }
            });
        }
        public async Task ShutdownAsync()
        {
            await DisconnectAsync();
            await worker.StopAsync();
        }
        public Task<ExportSnapshot> ExportPlcAsync(string directory, IProgress<ExportProgress> progress, CancellationToken cancellation, ExportOptions options = null)
        {
            return worker.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                var exporter = attached as IPlcExportSession;
                if (exporter == null) throw new InvalidOperationException("Collegare una sessione con esportazione PLC disponibile.");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var snapshot = exporter.ExportPlc(directory, progress, cancellation, options);
                snapshot.Metrics.ExportMilliseconds = timer.ElapsedMilliseconds;
                snapshot.Metrics.CrossReferenceCount = snapshot.CrossReferences.Count;
                return snapshot;
            });
        }
        public Task DisconnectAsync()
        {
            return worker.Run(() =>
            {
                // Retain the handle on a release failure so the user can retry.
                if (attached != null) { attached.Dispose(); attached = null; }
                return true;
            });
        }
    }
}
