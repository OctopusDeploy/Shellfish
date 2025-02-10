using System;
using System.Collections.Generic;
using System.Text;

namespace Octopus.Shellfish.Windows
{
    static class EnvironmentBlock
    {
        internal static Dictionary<string, string> GetEnvironmentVariablesForUser(AccessToken token, bool inheritFromCurrentProcess)
        {
            var env = IntPtr.Zero;

            Win32Helper.Invoke(() => Interop.Userenv.CreateEnvironmentBlock(out env, token.Handle, inheritFromCurrentProcess),
                $"Failed to load the environment variables for the user '{token.Username}'");

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
                Win32Helper.Invoke(() => Interop.Userenv.DestroyEnvironmentBlock(env),
                    $"Failed to destroy the environment variables structure for user '{token.Username}'");
            }

            return userEnvironment;
        }
    }
}