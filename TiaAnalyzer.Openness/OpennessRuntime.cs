using System;
using System.IO;
using System.Reflection;

namespace TiaAnalyzer.Openness
{
    public static class OpennessRuntime
    {
        private static bool configured;
        public static void Configure()
        {
            if (configured) return;
            configured = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }
        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            if (!requested.Name.StartsWith("Siemens.Engineering.", StringComparison.Ordinal) ||
                requested.Name.IndexOfAny(new[] { '/', '\\' }) >= 0) return null;
            var path = Path.Combine(SiemensEnvironmentProbe.LibraryPath, requested.Name + ".dll");
            if (!File.Exists(path)) return null;
            var actual = AssemblyName.GetAssemblyName(path);
            if (actual.Name != requested.Name || actual.Version == null || actual.Version.Major != 21 ||
                !string.Equals(actual.FullName, requested.FullName, StringComparison.OrdinalIgnoreCase)) return null;
            return Assembly.LoadFrom(path);
        }
    }
}
