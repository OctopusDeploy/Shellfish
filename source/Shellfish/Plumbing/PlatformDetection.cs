using System;
using System.Runtime.InteropServices;

namespace Octopus.Shellfish.Plumbing;

static class PlatformDetection
{
    public static bool IsRunningOnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}