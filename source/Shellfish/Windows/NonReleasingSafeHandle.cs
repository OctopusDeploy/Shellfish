using System;
using System.Runtime.InteropServices;

namespace Octopus.Shellfish.Windows;

class NonReleasingSafeHandle() : SafeHandle(IntPtr.Zero, false)
{
    public override bool IsInvalid => false;

    protected override bool ReleaseHandle() => true;
}