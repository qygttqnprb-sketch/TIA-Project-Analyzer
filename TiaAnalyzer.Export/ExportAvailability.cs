using System.Collections.Generic;

namespace TiaAnalyzer.Export
{
    public enum ExportFormat { Xml, Zip }

    // Future pipeline: read-only analysis -> XML documents -> ZIP package.
    // A plan is metadata only; V0.1 creates no files and calls no Siemens API.
    public sealed class ExportPlan
    {
        public ExportFormat Format { get; private set; }
        public IReadOnlyList<string> PlannedEntries { get; private set; }
        public ExportPlan(ExportFormat format)
        {
            Format = format;
            PlannedEntries = new List<string> { "manifest.xml", "components.xml", "diagnostics.xml" }.AsReadOnly();
        }
    }

    public interface IExportPlanner
    {
        bool CanExport { get; }
        ExportPlan Describe(ExportFormat format);
    }

    public sealed class FutureExportPlanner : IExportPlanner
    {
        public bool CanExport { get { return false; } }
        public ExportPlan Describe(ExportFormat format) { return new ExportPlan(format); }
    }

    public static class ExportAvailability
    {
        public static string Description { get { return "XML e ZIP previsti nella fase successiva. Nessun file viene scritto in V0.1."; } }
    }
}
