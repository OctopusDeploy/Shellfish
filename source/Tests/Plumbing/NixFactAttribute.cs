using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Tests.Plumbing
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NixFactAttribute : FactAttribute
    {
        public NixFactAttribute()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Skip = "This test only runs on Linux/macOS";
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NixTheoryAttribute : TheoryAttribute
    {
        public NixTheoryAttribute()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Skip = "This test only runs on Linux/macOS";
        }
    }
}
