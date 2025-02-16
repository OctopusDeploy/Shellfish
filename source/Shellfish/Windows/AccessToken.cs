using System;
using System.ComponentModel;

namespace Octopus.Shellfish.Windows;

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
        // See https://msdn.microsoft.com/en-us/library/windows/desktop/aa378184(v=vs.85).aspx
        var handle = LogonUser(username,
            domain,
            password,
            Interop.Advapi32.LogonType.Network,
            Interop.Advapi32.LogonProvider.Default);

        return new AccessToken(username, handle);
    }

    public void Dispose()
    {
        Handle.Dispose();
    }

    static SafeAccessTokenHandle LogonUser(string username,
        string domain,
        string password,
        Interop.Advapi32.LogonType logonType,
        Interop.Advapi32.LogonProvider logonProvider)
    {
        if (!Interop.Advapi32.LogonUser(username,
                domain,
                password,
                logonType,
                logonProvider,
                out var handle))
            throw new Win32Exception();

        return handle;
    }
}