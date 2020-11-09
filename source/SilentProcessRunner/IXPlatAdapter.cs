using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;

namespace Octopus.SilentProcessRunner
{
    interface IXPlatAdapter
    {
        void RunAsDifferentUser(ProcessStartInfo startInfo, NetworkCredential runAs, IDictionary<string, string>? customEnvironmentVariables);
        void TryKillProcessAndChildrenRecursively(Process process);
    }
}