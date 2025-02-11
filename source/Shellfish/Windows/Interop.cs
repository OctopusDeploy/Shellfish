using System;
using System.Runtime.InteropServices;

namespace Octopus.Shellfish.Windows;

// ReSharper disable InconsistentNaming

static class Interop
{
    static class Libraries
    {
        internal const string Advapi32 = "advapi32.dll";
        internal const string Kernel32 = "kernel32.dll";
        internal const string User32 = "user32.dll";
        internal const string Userenv = "userenv.dll";
    }

    internal static class Advapi32
    {
        // See https://msdn.microsoft.com/en-us/library/windows/desktop/aa378184(v=vs.85).aspx
        [DllImport(Libraries.Advapi32, SetLastError = true)]
        internal static extern bool LogonUser(
            string username,
            string domain,
            string password,
            LogonType logonType,
            LogonProvider logonProvider,
            out SafeAccessTokenHandle hToken);

        internal enum LogonProvider
        {
            Default = 0,
            WinNT40 = 2,
            WinNT50 = 3
        }

        internal enum LogonType
        {
            Interactive = 2,
            Network = 3,
            Batch = 4,
            Service = 5,
            Unlock = 7,
            NetworkClearText = 8,
            NewCredentials = 9
        }
    }

    internal static class Kernel32
    {
        [DllImport(Libraries.Kernel32, SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hHandle);

        [DllImport(Libraries.Kernel32, SetLastError = true)]
        internal static extern int GetCurrentThreadId();

        [DllImport(Libraries.Kernel32, SetLastError = true)]
        internal static extern bool GetCPInfoEx([MarshalAs(UnmanagedType.U4)] int codePage,
            [MarshalAs(UnmanagedType.U4)]
            int dwFlags,
            out CpInfoEx lpCPInfoEx);
        
        const int MAX_DEFAULTCHAR = 2;
        const int MAX_LEADBYTES = 12;
        const int MAX_PATH = 260;

        [StructLayout(LayoutKind.Sequential)]
        internal struct CpInfoEx
        {
            [MarshalAs(UnmanagedType.U4)]
            internal readonly int MaxCharSize;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_DEFAULTCHAR)]
            internal readonly byte[] DefaultChar;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LEADBYTES)]
            internal readonly byte[] LeadBytes;

            internal readonly char UnicodeDefaultChar;

            [MarshalAs(UnmanagedType.U4)]
            internal readonly int CodePage;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            internal readonly string CodePageName;
        }
    }

    internal static class User32
    {
        // Handles returned by GetProcessWindowStation and GetThreadDesktop should not be closed
        [DllImport(Libraries.User32, SetLastError = true)]
        internal static extern NonReleasingSafeHandle GetProcessWindowStation();

        [DllImport(Libraries.User32, SetLastError = true)]
        internal static extern NonReleasingSafeHandle GetThreadDesktop(int dwThreadId);
    }

    internal static class Userenv
    {
        // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762270(v=vs.85).aspx
        [DllImport(Libraries.Userenv, SetLastError = true)]
        internal static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, SafeAccessTokenHandle hToken, bool inheritFromCurrentProcess);

        // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762274(v=vs.85).aspx
        [DllImport(Libraries.Userenv, SetLastError = true)]
        internal static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);
        
        // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762281(v=vs.85).aspx
        [DllImport(Libraries.Userenv, SetLastError = true)]
        internal static extern bool LoadUserProfile(SafeAccessTokenHandle hToken, ref ProfileInfo lpProfileInfo);

        // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762282(v=vs.85).aspx
        // This function closes the registry handle for the user profile too
        [DllImport(Libraries.Userenv, SetLastError = true)]
        internal static extern bool UnloadUserProfile(SafeAccessTokenHandle hToken, IntPtr hProfile);

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProfileInfo
        {
            public int dwSize;
            public readonly int dwFlags;
            public string lpUserName;
            public readonly string lpProfilePath;
            public readonly string lpDefaultPath;
            public readonly string lpServerName;
            public readonly string lpPolicyPath;
            public readonly IntPtr hProfile;
        }
    }
}
// ReSharper restore InconsistentNaming
