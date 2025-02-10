#if !NET5_0_OR_GREATER
using System;

// ReSharper disable once CheckNamespace
namespace System.Runtime.Versioning;

[AttributeUsage(AttributeTargets.Assembly |
                AttributeTargets.Class |
                AttributeTargets.Constructor |
                AttributeTargets.Enum |
                AttributeTargets.Event |
                AttributeTargets.Field |
                AttributeTargets.Interface |
                AttributeTargets.Method |
                AttributeTargets.Module |
                AttributeTargets.Property |
                AttributeTargets.Struct,
                AllowMultiple = true, Inherited = false)]
// ReSharper disable once InconsistentNaming 
class SupportedOSPlatformAttribute(string platformName) : Attribute
{
    public string PlatformName { get; } = platformName;
}
#endif