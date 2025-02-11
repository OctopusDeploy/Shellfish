using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Octopus.Shellfish.Windows
{
    static class Win32Helper
    {
        public static void Invoke(Func<bool> nativeMethod, string failureDescription)
        {
            if (!nativeMethod())
            {
                throw new Exception(failureDescription, new Win32Exception());
            }
        }

        public static THandle Invoke<THandle>(Func<THandle> nativeMethod, string failureDescription)
            where THandle : SafeHandle
        {
            var result = nativeMethod();
            return !result.IsInvalid ? result : throw new Exception(failureDescription, new Win32Exception());
        }
    }
}