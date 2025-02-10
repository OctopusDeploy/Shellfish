using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Octopus.Shellfish.Windows
{
    sealed class SafeAccessTokenHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle()
            => Interop.Kernel32.CloseHandle(handle);
    }
}