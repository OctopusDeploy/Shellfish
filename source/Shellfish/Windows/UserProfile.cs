using System;
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
            var userProfile = new Interop.Userenv.ProfileInfo
            {
                lpUserName = token.Username
            };
            userProfile.dwSize = Marshal.SizeOf(userProfile);

            Win32Helper.Invoke(() => Interop.Userenv.LoadUserProfile(token.Handle, ref userProfile),
                $"Failed to load user profile for user '{token.Username}'");

            return new UserProfile(token, new SafeRegistryHandle(userProfile.hProfile, false));
        }

        void Unload()
        {
            Win32Helper.Invoke(() => Interop.Userenv.UnloadUserProfile(token.Handle, userProfile),
                $"Failed to unload user profile for user '{token.Username}'");
        }

        public void Dispose()
        {
            if (userProfile != null && !userProfile.IsClosed)
            {
                Unload();
                userProfile.Dispose();
            }
        }
    }
}