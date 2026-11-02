using System;
using System.Collections.Generic;

namespace TiaAnalyzer.Core
{
    // Bounded in-memory application log. No project data or files are written.
    public sealed class DiagnosticLog
    {
        private readonly Queue<string> entries = new Queue<string>();
        public void Add(string message)
        {
            if (entries.Count == 100) entries.Dequeue();
            entries.Enqueue(DateTime.Now.ToString("HH:mm:ss") + " · " + message);
        }
        public IReadOnlyList<string> Snapshot() { return new List<string>(entries).AsReadOnly(); }
    }
}
