using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TiaAnalyzer.Core;
using TiaAnalyzer.Export;
using TiaAnalyzer.Openness;

internal static class PackageChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-package-check-" + Guid.NewGuid().ToString("N"));
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var instances = await service.DiscoverAsync(); await service.ConnectAsync(instances[0]);
            var directory = Path.Combine(root, "snapshot");
            var snapshot = await service.ExportPlcAsync(directory, null, CancellationToken.None);
            File.WriteAllText(Path.Combine(directory, "unrelated-secret.txt"), "must not be packaged");
            var zip = PackageWriter.Build(directory, snapshot, "Perché il motore è fermo?", new PackageDiagnostics(), CancellationToken.None);
            using (var archive = ZipFile.OpenRead(zip))
            {
                check(archive.Entries.Count == 13 && archive.GetEntry("README.md") != null && archive.GetEntry("unrelated-secret.txt") == null,
                    "ZIP contains only declared XML and package documents");
                ExportSnapshot manifest;
                using (var stream = archive.GetEntry("manifest.json").Open()) manifest = JsonFiles.Read<ExportSnapshot>(stream);
                check(manifest.IsDemo && manifest.Question.Contains("motore"), "ZIP preserves demo provenance and user question");
                foreach (var artifact in manifest.Artifacts)
                    using (var stream = archive.GetEntry(artifact.RelativePath).Open())
                    using (var sha = SHA256.Create())
                        check(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == artifact.Sha256, "ZIP bytes match manifest hash for " + artifact.Name);
            }
            var before = File.ReadAllBytes(zip);
            bool overwrite = false;
            try { PackageWriter.CreateArchive(directory, zip, new[] { "manifest.json" }, CancellationToken.None); } catch (IOException) { overwrite = true; }
            check(overwrite && before.SequenceEqual(File.ReadAllBytes(zip)), "existing ZIP remains byte-for-byte unchanged");
            var failed = Path.Combine(root, "failed.zip");
            bool error = false;
            try { PackageWriter.CreateArchive(directory, failed, new[] { "missing.xml" }, CancellationToken.None); } catch (FileNotFoundException) { error = true; }
            check(error && !File.Exists(failed) && Directory.GetFiles(root, "*.partial").Length == 0, "archive failure publishes no ZIP and cleans its temporary file");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); bool cancelled = false;
                try { PackageWriter.CreateArchive(directory, failed, new[] { "manifest.json" }, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
                check(cancelled && !File.Exists(failed), "cancelled archive never appears complete");
            }
        }
        finally { await service.ShutdownAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static async Task GenerateDemo(string directory)
    {
        var service = new TiaConnectionService(new DemoBackend());
        try
        {
            var instances = await service.DiscoverAsync(); await service.ConnectAsync(instances[0]);
            var snapshot = await service.ExportPlcAsync(directory, null, CancellationToken.None);
            Console.WriteLine(PackageWriter.Build(directory, snapshot, "Il motore del nastro non parte in automatico. Trova i consensi e cita blocchi e reti.", new PackageDiagnostics(), CancellationToken.None));
        }
        finally { await service.ShutdownAsync(); }
    }
}
