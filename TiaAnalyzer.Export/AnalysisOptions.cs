namespace TiaAnalyzer.Export
{
    public sealed class AnalysisOptions
    {
        public TiaAnalyzer.Core.ExportRun Run { get; set; }
        public string CacheDirectory { get; set; }
        public bool IncludeNetworkXml { get; set; }
    }
}
