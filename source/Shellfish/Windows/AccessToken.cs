using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Octopus.Shellfish.Windows
{
    class AccessToken : IDisposable
    {
        public enum LogonProvider
        {
            Default = 0,
            WinNT40 = 2,
            WinNT50 = 3
        }

        public enum LogonType
        {
            Interactive = 2,
            Network = 3,
            Batch = 4,
            Service = 5,
            Unlock = 7,
            NetworkClearText = 8,
            NewCredentials = 9
        }

        AccessToken(string username, SafeAccessTokenHandle handle)
        {
            Username = username;
            Handle = handle;
        }

        public string Username { get; }
        public SafeAccessTokenHandle Handle { get; }

        public static AccessToken Logon(string username,
            string password,
            string domain = ".",
            LogonType logonType = LogonType.Network,
            LogonProvider logonProvider = LogonProvider.Default)
        {
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/aa378184(v=vs.85).aspx
            var handle = LogonUser(username, domain, password, LogonType.Network, LogonProvider.Default);

            return new AccessToken(username, handle);
        }

        public void Dispose()
        {
            Handle.Dispose();
        }

        static SafeAccessTokenHandle LogonUser(string username,
            string domain,
            string password,
            LogonType logonType,
            LogonProvider logonProvider)
        {
            if(!NativeMethods.LogonUser(username, domain, password, logonType, logonProvider, out var handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Logon failed for the user '{username}'");

            return handle;
        }
        
        static class NativeMethods
        {
            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool LogonUser(string username,
                string domain,
                string password,
                LogonType logonType,
                LogonProvider logonProvider,
                out SafeAccessTokenHandle hToken);
        }
    }
}