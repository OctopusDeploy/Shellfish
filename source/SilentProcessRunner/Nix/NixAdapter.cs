using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;

namespace Octopus.SilentProcessRunner.Nix
{
    class NixAdapter : IXPlatAdapter
    {
        public void RunAsDifferentUser(ProcessStartInfo startInfo, NetworkCredential runAs, IDictionary<string, string>? customEnvironmentVariables)
            => throw new PlatformNotSupportedException("NetCore on Linux or Mac does not support running a process as a different user.");
    }
}