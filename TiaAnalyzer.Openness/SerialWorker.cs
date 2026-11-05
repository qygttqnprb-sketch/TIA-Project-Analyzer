using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TiaAnalyzer.Openness
{
    internal sealed class SerialWorker
    {
        private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        private readonly TaskCompletionSource<bool> stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object gate = new object();
        private int workerThreadId;
        private bool started;
        private bool closing;
        public Task<T> Run<T>(Func<T> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == Volatile.Read(ref workerThreadId))
                throw new InvalidOperationException("Non accodare chiamate Openness dal worker stesso: attendere la coda causerebbe un deadlock.");
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                if (closing) throw new ObjectDisposedException(nameof(SerialWorker));
                queue.Add(() =>
                {
                    try { completion.SetResult(action()); }
                    catch (Exception ex) { completion.SetException(ex); }
                });
                if (!started)
                {
                    started = true;
                    var thread = new Thread(() =>
                    {
                        Volatile.Write(ref workerThreadId, Thread.CurrentThread.ManagedThreadId);
                        try { foreach (var work in queue.GetConsumingEnumerable()) work(); }
                        finally { queue.Dispose(); stopped.TrySetResult(true); }
                    }) { IsBackground = true, Name = "TIA Openness STA" };
#if NET5_0_OR_GREATER
                    if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
#else
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT) thread.SetApartmentState(ApartmentState.STA);
#endif
                    thread.Start();
                }
            }
            return completion.Task;
        }
        public Task StopAsync()
        {
            lock (gate)
            {
                if (!closing)
                {
                    closing = true;
                    queue.CompleteAdding();
                    if (!started) { queue.Dispose(); stopped.TrySetResult(true); }
                }
            }
            return stopped.Task;
        }
    }
}
