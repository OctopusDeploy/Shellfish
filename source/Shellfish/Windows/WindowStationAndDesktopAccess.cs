using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Octopus.Shellfish.Windows
{
    // Required to allow a service to run a process as another user
    // See http://stackoverflow.com/questions/677874/starting-a-process-with-credentials-from-a-windows-service/30687230#30687230
    [SupportedOSPlatform("Windows")]
    static class WindowStationAndDesktopAccess
    {
        public static void GrantAccessToWindowStationAndDesktop(string username, string? domainName = null)
        {
            var hWindowStation = GetProcessWindowStation();
            const int windowStationAllAccess = 0x000f037f;
            GrantAccess(username, domainName, hWindowStation, windowStationAllAccess);
            
            var hDesktop = GetThreadDesktop();
            const int desktopRightsAllAccess = 0x000f01ff;
            GrantAccess(username, domainName, hDesktop, desktopRightsAllAccess);
        }

        static void GrantAccess(string username, string? domainName, SafeHandle handle, int accessMask)
        {
            var security =
                new GenericSecurity(
                    false,
                    ResourceType.WindowObject,
                    handle,
                    AccessControlSections.Access);

            var account = string.IsNullOrEmpty(domainName)
                ? new NTAccount(username)
                : new NTAccount(domainName!, username);

            security.AddAccessRule(
                new GenericAccessRule(
                    account,
                    accessMask,
                    AccessControlType.Allow));
            security.Persist(handle, AccessControlSections.Access);
        }

        // Native API not available in UWP
        // All the code to manipulate a security object is available in .NET framework,
        // but its API tries to be type-safe and handle-safe, enforcing a special implementation
        // (to an otherwise generic WinAPI) for each handle type. This is to make sure
        // only a correct set of permissions can be set for corresponding object types and
        // mainly that handles do not leak.
        // Hence the AccessRule and the NativeObjectSecurity classes are abstract.
        // This is the simplest possible implementation that yet allows us to make use
        // of the existing .NET implementation, sparing necessity to
        // P/Invoke the underlying WinAPI.
        class GenericAccessRule : AccessRule
        {
            public GenericAccessRule(
                IdentityReference identity,
                int accessMask,
                AccessControlType type) :
                base(identity,
                    accessMask,
                    false,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    type)
            {
            }
        }

        class GenericSecurity : NativeObjectSecurity
        {
            public GenericSecurity(
                bool isContainer,
                ResourceType resType,
                SafeHandle objectHandle,
                AccessControlSections sectionsRequested)
                : base(isContainer, resType, objectHandle, sectionsRequested)
            {
            }

            public override Type AccessRightType => throw new NotImplementedException();

            public override Type AccessRuleType => typeof(AccessRule);

            public override Type AuditRuleType => typeof(AuditRule);

            public new void Persist(SafeHandle handle, AccessControlSections includeSections)
            {
#pragma warning disable PC001 // API not supported on all platforms
                base.Persist(handle, includeSections);
#pragma warning restore PC001 // API not supported on all platforms
            }

            public new void AddAccessRule(AccessRule rule)
            {
#pragma warning disable PC001 // API not supported on all platforms
                base.AddAccessRule(rule);
#pragma warning restore PC001 // API not supported on all platforms
            }

            public override AccessRule AccessRuleFactory(
                IdentityReference identityReference,
                int accessMask,
                bool isInherited,
                InheritanceFlags inheritanceFlags,
                PropagationFlags propagationFlags,
                AccessControlType type)
                => throw new NotImplementedException();

            public override AuditRule AuditRuleFactory(
                IdentityReference identityReference,
                int accessMask,
                bool isInherited,
                InheritanceFlags inheritanceFlags,
                PropagationFlags propagationFlags,
                AuditFlags flags)
                => throw new NotImplementedException();
        }

        static SafeHandle GetProcessWindowStation()
        {
            var handle = Interop.User32.GetProcessWindowStation();
            if (handle.IsInvalid) throw new Win32Exception();
            return handle;
        }

        static SafeHandle GetThreadDesktop()
        {
            var handle = Interop.User32.GetThreadDesktop(Interop.Kernel32.GetCurrentThreadId());
            if (handle.IsInvalid) throw new Win32Exception();
            return handle;
        }
    }
}