using System.Collections.Generic;

namespace TiaAnalyzer.Core
{
    public enum ExportMode { Full, Quick, Targeted }
    public sealed class ExportOptions
    {
        public ExportMode Mode { get; set; } = ExportMode.Full;
        public List<string> TargetObjects { get; set; } = new List<string>();
        public string ResumeDirectory { get; set; }
        public ExportRun Run { get; set; }
        public string CacheDirectory { get; set; }
        public bool ReuseUnchangedXml { get; set; }
        public bool CaptureCrossReferences { get; set; } = true;
    }
    public sealed class ObjectFingerprint
    {
        public string Kind { get; set; }
        public string Value { get; set; }
    }
    public sealed class ExportStamp
    {
        public string Token { get; set; }
        public bool ReliableForXml { get; set; }
        public List<ObjectFingerprint> Fingerprints { get; set; } = new List<ObjectFingerprint>();
    }
    // Preserve Siemens source/reference/location semantics, including members for
    // which UnderlyingObject is unavailable. No Siemens objects leave the worker.
    public sealed class PlcCrossReference
    {
        public string QueriedArtifactId { get; set; }
        public string Project { get; set; }
        public string Plc { get; set; }
        public string SourceName { get; set; }
        public string SourcePath { get; set; }
        public string SourceAddress { get; set; }
        public string ReferenceName { get; set; }
        public string ReferencePath { get; set; }
        public string ReferenceAddress { get; set; }
        public string ReferenceDevice { get; set; }
        public string Location { get; set; }
        public string Access { get; set; }
        public string ReferenceType { get; set; }
        public string Provider { get; set; }
    }
    public sealed class ExportMetrics
    {
        public List<string> SkippedPhases { get; set; } = new List<string>();
        public Dictionary<string, long> PhaseMilliseconds { get; set; } = new Dictionary<string, long>();
        public long ExportMilliseconds { get; set; }
        public long AnalysisMilliseconds { get; set; }
        public long DocumentsMilliseconds { get; set; }
        public long ArchivePayloadMilliseconds { get; set; }
        public long ManagedBytesBefore { get; set; }
        public long ManagedBytesAfter { get; set; }
        public int XmlReused { get; set; }
        public int XmlExported { get; set; }
        public int AnalysisCacheHits { get; set; }
        public int AnalysisCacheMisses { get; set; }
        public int CrossReferenceCount { get; set; }
        public int CacheWarnings { get; set; }
    }
}
