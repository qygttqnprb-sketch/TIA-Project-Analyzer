using System;
using System.IO;
using System.Linq;
using System.Threading;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Export
{
    [System.Runtime.Serialization.DataContract]
    internal sealed class AnalysisFragment
    {
        [System.Runtime.Serialization.DataMember]
        public string Version { get; set; }
        [System.Runtime.Serialization.DataMember]
        public string ContentHash { get; set; }
        [System.Runtime.Serialization.DataMember]
        public string ContentHashAlgorithm { get; set; }
        [System.Runtime.Serialization.DataMember]
        public ProjectAnalysis Analysis { get; set; }
    }
    internal sealed class AnalysisCache
    {
        internal const string ParserVersion = "0.7.2";
        private readonly ContentFileCache cache;
        public AnalysisCache(string directory) { cache = new ContentFileCache(Path.Combine(directory, "analysis-v1")); }
        private static string Key(ExportArtifact artifact, string hash, bool includeXml)
        {
            return ExportPaths.Identity(ParserVersion, artifact.Id, hash, artifact.Project, artifact.Plc, artifact.Group,
                artifact.RelativePath, artifact.Name, artifact.Kind, artifact.Language, artifact.Protected.ToString(), includeXml.ToString());
        }
        public bool Restore(string directory, ExportArtifact artifact, string hash, bool includeXml, CancellationToken cancellation,
            out AnalysisFragment fragment, out string warning)
        {
            fragment = null; var temporary = Path.Combine(directory, ".analysis-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                if (!cache.Restore(Key(artifact, hash, includeXml), temporary, cancellation, out warning)) return false;
                if (new FileInfo(temporary).Length > 64L * 1024 * 1024) throw new InvalidDataException("Frammento di analisi troppo grande.");
                using (var stream = File.OpenRead(temporary)) fragment = JsonFiles.Read<AnalysisFragment>(stream);
                if (fragment?.Version != ParserVersion || fragment.Analysis?.Blocks == null || fragment.Analysis.Symbols == null ||
                    fragment.Analysis.Dependencies == null || fragment.Analysis.Warnings == null || fragment.Analysis.Blocks.Count != 1 ||
                    fragment.Analysis.Assignments == null || fragment.Analysis.Calls == null || fragment.Analysis.Coverage == null ||
                    fragment.ContentHash == null || fragment.ContentHash.Length != 64 || fragment.ContentHashAlgorithm != "xml-tree-v2" || fragment.Analysis.Blocks[0]?.Networks == null ||
                    fragment.Analysis.Blocks[0].ArtifactId != artifact.Id || fragment.Analysis.Blocks[0].File != artifact.RelativePath ||
                    fragment.Analysis.Blocks[0].Project != artifact.Project || fragment.Analysis.Blocks[0].Plc != artifact.Plc ||
                    fragment.Analysis.Symbols.Any(s => s.ArtifactId != artifact.Id) || fragment.Analysis.Dependencies.Any(d => d.FromId != artifact.Id))
                    throw new InvalidDataException("Contesto cache non corrispondente.");
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Runtime.Serialization.SerializationException)
            { warning = "Cache analisi ignorata: " + ex.Message; fragment = null; return false; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Save(string directory, ExportArtifact artifact, string hash, bool includeXml, AnalysisFragment fragment, CancellationToken cancellation)
        {
            var temporary = Path.Combine(directory, ".analysis-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                JsonFiles.Write(temporary, fragment);
                if (new FileInfo(temporary).Length <= 64L * 1024 * 1024) cache.Save(Key(artifact, hash, includeXml), temporary, cancellation);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
