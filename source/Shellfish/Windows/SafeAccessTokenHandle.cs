using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Octopus.Shellfish.Windows
{
    sealed class SafeAccessTokenHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle()
            => CloseHandle(handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hHandle);
    }
}