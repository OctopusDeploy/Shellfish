using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Octopus.Shellfish.Windows
{
    [SupportedOSPlatform("Windows")]
    class UserProfile : IDisposable
    {
        readonly AccessToken token;
        readonly SafeRegistryHandle userProfile;

        UserProfile(AccessToken token, SafeRegistryHandle userProfile)
        {
            this.token = token;
            this.userProfile = userProfile;
        }

        public static UserProfile Load(AccessToken token)
        {
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762281(v=vs.85).aspx
            var userProfile = LoadUserProfile(token.Handle, token.Username);

            return new UserProfile(token, new SafeRegistryHandle(userProfile.hProfile, false));
        }

        void Unload()
        {
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762282(v=vs.85).aspx
            // This function closes the registry handle for the user profile too
            UnloadUserProfile(token.Handle, userProfile);
        }

        public void Dispose()
        {
            if (userProfile != null && !userProfile.IsClosed)
            {
                Unload();
                userProfile.Dispose();
            }
        }

        static Interop.Userenv.ProfileInfo LoadUserProfile(SafeAccessTokenHandle hToken, string username)
        {
            var userProfile = new Interop.Userenv.ProfileInfo
            {
                lpUserName = username
            };
            userProfile.dwSize = Marshal.SizeOf(userProfile);

            if (!Interop.Userenv.LoadUserProfile(hToken, ref userProfile))
                throw new Win32Exception();

            return userProfile;
        }

        static void UnloadUserProfile(SafeAccessTokenHandle hToken, SafeRegistryHandle hProfile)
        {
            if (!Interop.Userenv.UnloadUserProfile(hToken, hProfile))
                throw new Win32Exception();
        }
    }
}