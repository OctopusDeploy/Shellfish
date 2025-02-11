using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Octopus.Shellfish.Windows
{
    static class EnvironmentBlock
    {
        internal static Dictionary<string, string> GetEnvironmentVariablesForUser(AccessToken token, bool inheritFromCurrentProcess)
        {
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762270(v=vs.85).aspx
            var env = CreateEnvironmentBlock(token.Handle, inheritFromCurrentProcess);

            var userEnvironment = new Dictionary<string, string>();
            try
            {
                var testData = new StringBuilder();
                unsafe
                {
                    // The environment block is an array of null-terminated Unicode strings.
                    // Key and Value are separated by =
                    // The list ends with two nulls (\0\0).
                    var start = (short*)env.ToPointer();
                    var done = false;
                    var current = start;
                    while (!done)
                    {
                        if (testData.Length > 0 && *current == 0 && current != start)
                        {
                            var data = testData.ToString();
                            var index = data.IndexOf('=');
                            if (index == -1)
                                userEnvironment.Add(data, "");
                            else if (index == data.Length - 1)
                                userEnvironment.Add(data.Substring(0, index), "");
                            else
                                userEnvironment.Add(data.Substring(0, index), data.Substring(index + 1));
                            testData.Length = 0;
                        }

                        if (*current == 0 && current != start && *(current - 1) == 0)
                            done = true;
                        if (*current != 0)
                            testData.Append((char)*current);
                        current++;
                    }
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
            if (!NativeMethods.CreateEnvironmentBlock(out IntPtr lpEnvironment, hToken, inheritFromCurrentProcess))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to load user environment variables");

            return lpEnvironment;
        }

        static void DestroyEnvironmentBlock(IntPtr lpEnvironment)
        {
            if (!NativeMethods.DestroyEnvironmentBlock(lpEnvironment))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to destroy environment block");
        }

        static class NativeMethods
        {
            [DllImport("userenv.dll", SetLastError = true)]
            public static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, SafeAccessTokenHandle hToken, bool inheritFromCurrentProcess);

            [DllImport("userenv.dll", SetLastError = true)]
            public static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);
        }
    }
}