using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;

namespace Octopus.SilentProcessRunner.Windows
{
    class WindowsAdapter : IXPlatAdapter
    {
        public void RunAsDifferentUser(ProcessStartInfo startInfo, NetworkCredential runAs, IDictionary<string, string>? customEnvironmentVariables)
        {
#pragma warning disable PC001 // API not supported on all platforms
            startInfo.UserName = runAs.UserName;
            startInfo.Domain = runAs.Domain;
            startInfo.Password = runAs.SecurePassword;
            startInfo.LoadUserProfile = true;
#pragma warning restore PC001 // API not supported on all platforms

            WindowStationAndDesktopAccess.GrantAccessToWindowStationAndDesktop(runAs.UserName, runAs.Domain);

            if (customEnvironmentVariables != null && customEnvironmentVariables.Any())
                WindowsEnvironmentVariableHelper.SetEnvironmentVariablesForTargetUser(startInfo, runAs, customEnvironmentVariables);
        }
    }
}