using System;

namespace Octopus.Shellfish.Windows
{
    class AccessToken : IDisposable
    {
        AccessToken(string username, SafeAccessTokenHandle handle)
        {
            Username = username;
            Handle = handle;
        }

        public string Username { get; }
        public SafeAccessTokenHandle Handle { get; }

        public static AccessToken Logon(string username, string password, string domain = ".")
        {
            SafeAccessTokenHandle? handle = null;
            Win32Helper.Invoke(() => Interop.Advapi32.LogonUser(username,
                    domain,
                    password,
                    Interop.Advapi32.LogonType.Network,
                    Interop.Advapi32.LogonProvider.Default,
                    out handle),
                $"Logon failed for the user '{username}'");

            return new AccessToken(username, handle!);
        }

        public void Dispose()
        {
            Handle?.Dispose();
        }
    }
}