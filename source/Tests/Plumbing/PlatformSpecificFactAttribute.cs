using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Tests.Plumbing
{
    public enum Platform
    {
        Windows,
        Linux, // macOS is considered to be linux
    }
    
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PlatformSpecificFactAttribute : FactAttribute
    {
        public PlatformSpecificFactAttribute(Platform platform)
        {
            if(!RuntimeInformation.IsOSPlatform(MapToOSPlatform(platform))) Skip = $"This test only runs on {platform}";
        }
        
        // ReSharper disable once InconsistentNaming
        public static OSPlatform MapToOSPlatform(Platform platform)
        {
            return platform switch
            {
                Platform.Windows => OSPlatform.Windows,
                Platform.Linux => OSPlatform.Linux,
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
            };
        }
    }
    
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PlatformSpecificTheoryAttribute : TheoryAttribute
    {
        public PlatformSpecificTheoryAttribute(Platform platform)
        {
            if(!RuntimeInformation.IsOSPlatform(PlatformSpecificFactAttribute.MapToOSPlatform(platform))) Skip = $"This test only runs on {platform}";
        }
    }
}