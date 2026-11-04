using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

internal static class TestFiles
{
    public static ZipArchive OpenArchiveForUpdate(string path)
    {
        // Permit background scanners to read these test-owned fixtures on Windows.
        var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try { return new ZipArchive(file, ZipArchiveMode.Update); }
        catch { file.Dispose(); throw; }
    }
    public static void DeleteOwnedTemporary(string directory, string prefix)
    {
        var path = Path.GetFullPath(directory);
        if (!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), prefix), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test cleanup path.");
        for (int attempt = 0; ; attempt++)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); return; }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 5)
            { Thread.Sleep(50 * (attempt + 1)); }
        }
    }
}
