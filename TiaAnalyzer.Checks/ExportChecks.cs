using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

internal static class ExportChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-export-check-" + Guid.NewGuid().ToString("N"));
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            bool disconnected = false;
            try { await service.ExportPlcAsync(directory, null, CancellationToken.None); } catch (InvalidOperationException) { disconnected = true; }
            check(disconnected && !Directory.Exists(directory), "export requires an attached session");
            var instances = await service.DiscoverAsync();
            await service.ConnectAsync(instances[0]);
            var snapshot = await service.ExportPlcAsync(directory, null, CancellationToken.None);
            check(snapshot.IsDemo && snapshot.Artifacts.Count == 6 && snapshot.Components.Count == 6, "demo exports synthetic OB FB FC DB UDT and tags");
            check(snapshot.Artifacts.All(a => a.Status == "Exported" && File.Exists(ExportPaths.Resolve(directory, a.RelativePath))), "XML artifact paths exist and have explicit outcomes");
            SnapshotStore.Complete(directory, snapshot, CancellationToken.None);
            ExportSnapshot roundtrip;
            using (var stream = File.OpenRead(Path.Combine(directory, "manifest.json"))) roundtrip = JsonFiles.Read<ExportSnapshot>(stream);
            check(roundtrip.IsDemo && roundtrip.Artifacts.All(a => a.Sha256.Length == 64) && roundtrip.Artifacts[0].Plc == "PLC_Conveyor", "manifest roundtrip preserves scope provenance and SHA256");
            bool overwrite = false;
            try { await service.ExportPlcAsync(directory, null, CancellationToken.None); } catch (IOException) { overwrite = true; }
            check(overwrite, "export refuses to overwrite nonempty folders");
            var failedDirectory = Path.Combine(directory, "failure");
            var writer = new PlcExportWriter(failedDirectory, true, null, CancellationToken.None);
            writer.Write("P", "PLC", "Nested/A", "A:B", "FB", "SCL", false, file => { File.WriteAllText(file.FullName, "partial"); throw new IOException("inconsistent block"); });
            writer.Write("P", "PLC", "Nested/B", "A?B", "FB", "SCL", true, file => File.WriteAllText(file.FullName, "<Document/>"));
            check(writer.Snapshot.Artifacts[0].Status == "Failed" && writer.Snapshot.Artifacts[0].RelativePath == null &&
                writer.Snapshot.Issues.Count == 1 && Directory.GetFiles(failedDirectory, "*.xml", SearchOption.AllDirectories).Length == 1,
                "failed item records error removes partial XML and continues");
            check(writer.Snapshot.Artifacts[1].Status == "InterfaceOnly", "know-how protected interface is explicitly marked");
            check(ExportPaths.Segment("A:B") != ExportPaths.Segment("A?B") && ExportPaths.Segment("CON").StartsWith("x_") &&
                ExportPaths.Segment("Motor") != ExportPaths.Segment("motor"), "Windows filenames disambiguate invalid characters reserved names and case");
            check(ExportPaths.Identity("a\nb", "c") != ExportPaths.Identity("a", "b\nc"), "artifact identities cannot collide through delimiter characters");
            var blockedDirectory = Path.Combine(directory, "blocked");
            var blockedWriter = new PlcExportWriter(blockedDirectory, true, null, CancellationToken.None);
            File.WriteAllText(Path.Combine(blockedDirectory, "plc"), "a file blocks directory creation");
            blockedWriter.Write("P", "PLC", "Blocks", "CannotCreateFolder", "FB", "SCL", false, file => File.WriteAllText(file.FullName, "<Document/>"));
            check(blockedWriter.Snapshot.Artifacts[0].RelativePath == null && blockedWriter.Snapshot.Issues.Count == 1, "directory creation failure cannot leave a usable artifact path");
            foreach (var path in new[] { "../bad", "C:/bad", "/bad", "plc/../../bad", "plc\\bad" })
            {
                bool rejected = false;
                try { ExportPaths.Resolve(directory, path); } catch (IOException) { rejected = true; }
                check(rejected, "reject unsafe export path " + path);
            }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool cancelled = false;
                try { await service.ExportPlcAsync(Path.Combine(directory, "cancelled"), null, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                check(cancelled && !Directory.Exists(Path.Combine(directory, "cancelled")) && service.HasSession, "cancellation preserves session and creates no completed snapshot");
            }
        }
        finally
        {
            await service.ShutdownAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
