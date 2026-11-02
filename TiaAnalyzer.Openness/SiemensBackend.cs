using System;
using System.Collections.Generic;
using TiaAnalyzer.Core;

namespace TiaAnalyzer.Openness
{
    // This fallback is the only Siemens backend compiled without local V21 DLLs.
#if !SIEMENS_V21
    public sealed class SiemensBackend : ITiaBackend
    {
        public bool IsAvailable { get { return false; } }
        public IReadOnlyList<TiaInstance> Discover() { throw Unavailable(); }
        public IAttachedPortal Attach(TiaInstance instance) { throw Unavailable(); }
        private static Exception Unavailable()
        {
            return new TiaConnectionException(ConnectionFailure.ApiUnavailable,
                "Adapter Siemens non incluso: ricompilare su Windows con EnableSiemensOpenness=true e DLL V21 locali.");
        }
    }
#endif
}
