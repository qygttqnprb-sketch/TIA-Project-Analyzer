using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace TiaAnalyzer.Core
{
    public sealed class ExportDiagnosticsException : IOException
    { public ExportDiagnosticsException(string message, Exception inner) : base(message, inner) { } }
    public sealed class RunEvent
    {
        public string ExportId { get; set; }
        public long Sequence { get; set; }
        public string OperationId { get; set; }
        public string Operation { get; set; }
        public string Phase { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Path { get; set; }
        public string StartedUtc { get; set; }
        public string EndedUtc { get; set; }
        public long DurationMilliseconds { get; set; }
        public string Outcome { get; set; }
        public string LastCompleted { get; set; }
        public string Detail { get; set; }
    }
    public sealed class RunStatus
    {
        public string ExportId { get; set; }
        public int Completed { get; set; }
        public int? Total { get; set; }
        public string Operation { get; set; }
        public string LastCompleted { get; set; }
        public string Checkpoint { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public long OperationMilliseconds { get; set; }
        public List<string> Events { get; set; }
    }
    // Timer sees only immutable metadata/Stopwatch values, never engineering objects.
    // Each Start is durably flushed BEFORE the supplied action is entered.
    public sealed class ExportRun : IDisposable
    {
        private readonly object gate = new object(), ioGate = new object();
        private readonly FileStream stream;
        private readonly Timer watchdog;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly List<Operation> active = new List<Operation>();
        private readonly Queue<string> recent = new Queue<string>();
        private readonly Dictionary<string, long> timings = new Dictionary<string, long>();
        private long sequence;
        private bool disposed;
        private Exception logFailure;
        private string lastCompleted = "Nessuno", checkpoint = "Nessuno";
        private int completed;
        private int? total;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string DirectoryPath { get; }
        public ExportRun(string directory)
        {
            DirectoryPath = Path.GetFullPath(directory); Directory.CreateDirectory(DirectoryPath);
            stream = new FileStream(Path.Combine(DirectoryPath, "operations.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            try { Emit(new RunEvent { Operation = "Run", Phase = "Start", StartedUtc = DateTime.UtcNow.ToString("O"), Outcome = "Incomplete" }); }
            catch { stream.Dispose(); throw; }
            watchdog = new Timer(_ => { try { PollWatchdog(); } catch (Exception ex) { lock (gate) logFailure = ex; } }, null, 1000, 1000);
        }
        public Operation Begin(string operation, string phase, string name = "", string kind = "", string path = "")
        {
            var item = new Operation(this, operation, phase, name, kind, path);
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ExportRun));
                if (logFailure != null) throw new ExportDiagnosticsException("Log diagnostico non scrivibile: nessuna nuova chiamata.", logFailure);
                active.Add(item);
            }
            try { Emit(item.Event("Start", null)); return item; }
            catch { lock (gate) active.Remove(item); throw; }
        }
        public T Call<T>(string operation, string phase, string name, string kind, string path, Func<T> action)
        {
            using (var scope = Begin(operation, phase, name, kind, path))
                try { var result = action(); scope.Success(); return result; }
                catch (Exception ex) { scope.Fail(ex); throw; }
        }
        public void Call(string operation, string phase, string name, string kind, string path, Action action)
        { Call(operation, phase, name, kind, path, () => { action(); return true; }); }
        private void Emit(RunEvent item)
        {
            lock (ioGate)
            {
                lock (gate) { if (disposed) return; item.ExportId = Id; item.Sequence = ++sequence; item.LastCompleted = lastCompleted; }
                try
                {
                    using (var buffer = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(RunEvent)).WriteObject(buffer, item);
                        var bytes = buffer.ToArray(); stream.Write(bytes, 0, bytes.Length); stream.WriteByte(10); stream.Flush(true);
                    }
                }
                catch (Exception ex)
                {
                    lock (gate) logFailure = ex;
                    throw new ExportDiagnosticsException("Log non salvato; nessuna nuova chiamata Siemens consentita.", ex);
                }
                if (item.Outcome != "Watchdog") Remember(item);
            }
        }
        private void Remember(RunEvent item)
        {
            lock (gate)
            {
                if (recent.Count == 5) recent.Dequeue();
                recent.Enqueue(DateTime.Now.ToString("HH:mm:ss") + " " + item.Outcome + " · " + item.Operation + " · " + item.Name + " " + item.Detail);
            }
        }
        public void PollWatchdog() { PollWatchdog(null); }
        // Deterministic elapsed override for tests only. Production uses monotonic Stopwatches.
        public void PollWatchdog(TimeSpan? elapsedOverride)
        {
            var warnings = new List<RunEvent>();
            lock (gate)
            {
                if (disposed) return;
                foreach (var item in active)
                    foreach (var threshold in new[] { 30, 120, 300 })
                        if ((elapsedOverride?.TotalSeconds ?? item.Clock.Elapsed.TotalSeconds) >= threshold && item.Warned.Add(threshold))
                        { var entry = item.Event("Watchdog", null); entry.Detail = "Attesa >= " + threshold + " s (operazione o I/O diagnostico); nessuna interruzione forzata."; warnings.Add(entry); Remember(entry); }
            }
            foreach (var warning in warnings) Emit(warning);
        }
        public void Inventory(int count) { lock (gate) total = count; }
        public void Completed(string name) { lock (gate) { completed++; lastCompleted = name; } }
        public void Checkpoint(string path) { lock (gate) checkpoint = path + " · " + DateTime.UtcNow.ToString("O"); }
        public RunStatus Status()
        {
            lock (gate)
            {
                var current = active.LastOrDefault();
                var events = recent.ToList();
                if (logFailure != null) { if (events.Count == 5) events.RemoveAt(0); events.Add("ERRORE LOG: " + logFailure.Message); }
                return new RunStatus { ExportId = Id, Completed = completed, Total = total, LastCompleted = lastCompleted,
                    Checkpoint = checkpoint, ElapsedMilliseconds = elapsed.ElapsedMilliseconds,
                    Operation = current == null ? "In attesa / pipeline locale" : current.Name + " · " + current.NameOfOperation,
                    OperationMilliseconds = current?.Clock.ElapsedMilliseconds ?? 0, Events = events };
            }
        }
        public Dictionary<string, long> Timings() { lock (gate) return new Dictionary<string, long>(timings); }
        public void Finish(string state, string detail = null) { Emit(new RunEvent { Operation = "Run", Phase = "End", EndedUtc = DateTime.UtcNow.ToString("O"), DurationMilliseconds = elapsed.ElapsedMilliseconds, Outcome = state, Detail = detail }); }
        public void Dispose()
        {
            lock (ioGate) { lock (gate) { if (disposed) return; disposed = true; } watchdog.Dispose(); stream.Dispose(); }
        }
        public sealed class Operation : IDisposable
        {
            private readonly ExportRun owner;
            private readonly string phase, kind, path, started = DateTime.UtcNow.ToString("O"), id = Guid.NewGuid().ToString("N");
            private string outcome = "Incomplete", detail;
            internal readonly Stopwatch Clock = Stopwatch.StartNew();
            internal readonly HashSet<int> Warned = new HashSet<int>();
            internal string Name { get; }
            internal string NameOfOperation { get; }
            internal Operation(ExportRun owner, string operation, string phase, string name, string kind, string path)
            { this.owner = owner; NameOfOperation = operation; this.phase = phase; Name = name; this.kind = kind; this.path = path; }
            internal RunEvent Event(string result, string message)
            { return new RunEvent { OperationId = id, Operation = NameOfOperation, Phase = phase, Name = Name, Kind = kind, Path = path,
                StartedUtc = started, EndedUtc = result == "Start" || result == "Watchdog" ? null : DateTime.UtcNow.ToString("O"),
                DurationMilliseconds = Clock.ElapsedMilliseconds, Outcome = result, Detail = message }; }
            public void Success() { outcome = "Succeeded"; }
            public void Fail(Exception ex) { outcome = ex is OperationCanceledException ? "Cancelled" : "Failed"; detail = ex.GetType().Name + ": " + ex.Message; }
            public void Dispose()
            {
                lock (owner.gate)
                {
                    owner.active.Remove(this);
                    long value; owner.timings.TryGetValue(phase, out value); if (phase != "Traversal" && !owner.active.Any(a => a.phase == phase)) owner.timings[phase] = value + Clock.ElapsedMilliseconds;
                }
                owner.Emit(Event(outcome, detail));
            }
        }
    }
}
