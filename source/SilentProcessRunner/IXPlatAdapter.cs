using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Octopus.SilentProcessRunner
{
    interface IXPlatAdapter
    {
        void RunAsDifferentUser(ProcessStartInfo startInfo, NetworkCredential runAs, IDictionary<string, string>? customEnvironmentVariables);
        void TryKillProcessAndChildrenRecursively(Process process);
        Encoding GetOemEncoding();
    }
}