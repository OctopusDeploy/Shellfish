using System;
using System.Runtime.InteropServices;

namespace Octopus.SilentProcessRunner
{
    static class PlatformDetection
    {
        public static bool IsRunningOnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}