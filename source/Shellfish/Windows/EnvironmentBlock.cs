using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Octopus.Shellfish.Windows;

static class EnvironmentBlock
{
    static readonly char[] Separators = ['='];

    internal static Dictionary<string, string> GetEnvironmentVariablesForUser(AccessToken token, bool inheritFromCurrentProcess)
    {
        // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762270(v=vs.85).aspx
        var env = CreateEnvironmentBlock(token.Handle, inheritFromCurrentProcess);

        var userEnvironment = new Dictionary<string, string>();
        try
        {
            // The environment block is an array of null-terminated Unicode strings.
            // Key and Value are separated by =
            // The list ends with two nulls (\0\0).
            var ptr = env;
            var str = Marshal.PtrToStringUni(ptr);
            while (str?.Length > 0)
            {
                var vals = str.Split(Separators, 2);
                userEnvironment.Add(vals[0], vals[1]);

                // advance pointer to the end of the current string
                // two bytes per character plus two-byte null terminator
                ptr = IntPtr.Add(ptr, str.Length * 2 + 2);
                str = Marshal.PtrToStringUni(ptr);
            }
        }
        finally
        {
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762274(v=vs.85).aspx
            DestroyEnvironmentBlock(env);
        }

        return userEnvironment;
    }

    static IntPtr CreateEnvironmentBlock(SafeAccessTokenHandle hToken, bool inheritFromCurrentProcess)
    {
        if (!Interop.Userenv.CreateEnvironmentBlock(out var lpEnvironment, hToken, inheritFromCurrentProcess))
            throw new Win32Exception();

        return lpEnvironment;
    }

    static void DestroyEnvironmentBlock(IntPtr lpEnvironment)
    {
        if (!Interop.Userenv.DestroyEnvironmentBlock(lpEnvironment))
            throw new Win32Exception();
    }
}