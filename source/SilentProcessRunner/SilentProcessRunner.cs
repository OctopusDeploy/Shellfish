using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Octopus.Diagnostics;
using Octopus.SilentProcessRunner.Nix;
using Octopus.SilentProcessRunner.Windows;

namespace Octopus.SilentProcessRunner
{
    public static class SilentProcessRunner
    {
        static readonly IXPlatAdapter XPlatAdapter = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (IXPlatAdapter)new WindowsAdapter()
            : new NixAdapter();

        public static int ExecuteCommand(
            string executable,
            string arguments,
            string workingDirectory,
            Action<string> debug,
            Action<string> info,
            Action<string> error,
            NetworkCredential? runAs = null,
            IDictionary<string, string>? customEnvironmentVariables = null,
            CancellationToken cancel = default)
        {
            if (executable == null)
                throw new ArgumentNullException(nameof(executable));
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            if (workingDirectory == null)
                throw new ArgumentNullException(nameof(workingDirectory));
            if (debug == null)
                throw new ArgumentNullException(nameof(debug));
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            customEnvironmentVariables ??= new Dictionary<string, string>();

            void WriteData(Action<string> action, ManualResetEventSlim resetEvent, DataReceivedEventArgs e)
            {
                try
                {
                    if (e.Data == null)
                    {
                        resetEvent.Set();
                        return;
                    }

                    action(e.Data);
                }
                catch (Exception ex)
                {
                    try
                    {
                        error($"Error occurred handling message: {ex.PrettyPrint()}");
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }

            try
            {
                // We need to be careful to make sure the message is accurate otherwise people could wrongly assume the exe is in the working directory when it could be somewhere completely different!
                var executableDirectoryName = Path.GetDirectoryName(executable);
                debug($"Executable directory is {executableDirectoryName}");

                var exeInSamePathAsWorkingDirectory = string.Equals(executableDirectoryName?.TrimEnd('\\', '/'), workingDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
                var exeFileNameOrFullPath = exeInSamePathAsWorkingDirectory ? Path.GetFileName(executable) : executable;
                debug($"Executable name or full path: {exeFileNameOrFullPath}");

                var encoding = EncodingDetector.GetOEMEncoding();
                var hasCustomEnvironmentVariables = customEnvironmentVariables.Any();

                bool runAsSameUser;
                string runningAs;
                if (runAs == null)
                {
                    debug("No user context provided. Running as current user.");
                    runAsSameUser = true;
                    runningAs = $@"{ProcessIdentity.CurrentUserName}";
                }
                else
                {
                    runAsSameUser = false;
                    runningAs = $@"{runAs.Domain ?? Environment.MachineName}\{runAs.UserName}";
                    debug($"Different user context provided. Running as {runningAs}");
                }

                var customEnvironmentVarsMessage = hasCustomEnvironmentVariables
                    ? runAsSameUser
                        ? $"the same environment variables as the launching process plus {customEnvironmentVariables.Count} custom variable(s)"
                        : $"that user's environment variables plus {customEnvironmentVariables.Count} custom variable(s)"
                    : runAsSameUser
                        ? "the same environment variables as the launching process"
                        : "that user's default environment variables";

                debug($"Starting {exeFileNameOrFullPath} in working directory '{workingDirectory}' using '{encoding.EncodingName}' encoding running as '{runningAs}' with {customEnvironmentVarsMessage}");

                using (var outputResetEvent = new ManualResetEventSlim(false))
                using (var errorResetEvent = new ManualResetEventSlim(false))
                using (var process = new System.Diagnostics.Process())
                {
                    process.StartInfo.FileName = executable;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WorkingDirectory = workingDirectory;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    if (runAs == null)
                        RunAsSameUser(process.StartInfo, customEnvironmentVariables);
                    else
                        XPlatAdapter.RunAsDifferentUser(process.StartInfo, runAs, customEnvironmentVariables);

                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    if (PlatformDetection.IsRunningOnWindows)
                    {
                        process.StartInfo.StandardOutputEncoding = encoding;
                        process.StartInfo.StandardErrorEncoding = encoding;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        WriteData(info, outputResetEvent, e);
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        WriteData(error, errorResetEvent, e);
                    };

                    process.Start();

                    var running = true;

                    using (cancel.Register(() =>
                    {
                        if (running) DoOurBestToCleanUp(process, error);
                    }))
                    {
                        if (cancel.IsCancellationRequested)
                            DoOurBestToCleanUp(process, error);

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();

                        SafelyCancelRead(process.CancelErrorRead, debug);
                        SafelyCancelRead(process.CancelOutputRead, debug);

                        SafelyWaitForAllOutput(outputResetEvent, cancel, debug);
                        SafelyWaitForAllOutput(errorResetEvent, cancel, debug);

                        var exitCode = SafelyGetExitCode(process);
                        debug($"SilentProcessRunner {exeFileNameOrFullPath} in {workingDirectory} exited with code {exitCode}");

                        running = false;
                        return exitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error when attempting to execute {executable}: {ex.Message}", ex);
            }
        }

        static int SafelyGetExitCode(System.Diagnostics.Process process)
        {
            try
            {
                return process.ExitCode;
            }
            catch (InvalidOperationException ex) when (ex.Message == "No process is associated with this object.")
            {
                return -1;
            }
        }

        static void SafelyWaitForAllOutput(ManualResetEventSlim outputResetEvent,
            CancellationToken cancel,
            Action<string> debug)
        {
            try
            {
                //5 seconds is a bit arbitrary, but the process should have already exited by now, so unwise to wait too long
                outputResetEvent.Wait(TimeSpan.FromSeconds(5), cancel);
            }
            catch (OperationCanceledException ex)
            {
                debug($"Swallowing {ex.GetType().Name} while waiting for last of the process output.");
            }
        }

        static void SafelyCancelRead(Action action, Action<string> debug)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException ex)
            {
                debug($"Swallowing {ex.GetType().Name} calling {action.Method.Name}.");
            }
        }

        public static void ExecuteCommandWithoutWaiting(
            string executable,
            string arguments,
            string workingDirectory,
            NetworkCredential? runAs = null,
            IDictionary<string, string>? customEnvironmentVariables = null)
        {
            try
            {
                using (var process = new System.Diagnostics.Process())
                {
                    process.StartInfo.FileName = executable;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WorkingDirectory = workingDirectory;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    if (runAs == null)
                        RunAsSameUser(process.StartInfo, customEnvironmentVariables);
                    else
                        XPlatAdapter.RunAsDifferentUser(process.StartInfo, runAs, customEnvironmentVariables);

                    process.Start();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error when attempting to execute {executable}: {ex.Message}", ex);
            }
        }

        static void RunAsSameUser(ProcessStartInfo processStartInfo, IDictionary<string, string>? customEnvironmentVariables)
        {
            // Accessing the ProcessStartInfo.EnvironmentVariables dictionary will pre-load the environment variables for the current process
            // Then we'll add/overwrite with the customEnvironmentVariables
            if (customEnvironmentVariables != null && customEnvironmentVariables.Any())
                foreach (var variable in customEnvironmentVariables)
                    processStartInfo.EnvironmentVariables[variable.Key] = variable.Value;
        }

        static void DoOurBestToCleanUp(System.Diagnostics.Process process, Action<string> error)
        {
            try
            {
                XPlatAdapter.TryKillProcessAndChildrenRecursively(process);
            }
            catch (Exception hitmanException)
            {
                error($"Failed to kill the launched process and its children: {hitmanException}");
                try
                {
                    process.Kill();
                }
                catch (Exception killProcessException)
                {
                    error($"Failed to kill the launched process: {killProcessException}");
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
#pragma warning disable PC003 // Native API not available in UWP
        static extern bool GetCPInfoEx([MarshalAs(UnmanagedType.U4)]
            int codePage,
            [MarshalAs(UnmanagedType.U4)]
            int dwFlags,
            out CPINFOEX lpCPInfoEx);
#pragma warning restore PC003 // Native API not available in UWP

        internal class EncodingDetector
        {
            public static Encoding GetOEMEncoding()
            {
                var defaultEncoding = Encoding.UTF8;

                if (PlatformDetection.IsRunningOnWindows)
                    try
                    {
                        // Get the OEM CodePage for the installation, otherwise fall back to code page 850 (DOS Western Europe)
                        // https://en.wikipedia.org/wiki/Code_page_850
                        const int CP_OEMCP = 1;
                        const int dwFlags = 0;
                        const int CodePage850 = 850;

                        var codepage = GetCPInfoEx(CP_OEMCP, dwFlags, out var info) ? info.CodePage : CodePage850;

#if REQUIRES_CODE_PAGE_PROVIDER
                        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(codepage);    // When it says that this can return null, it *really can* return null.
                        return encoding ?? defaultEncoding;
#else
                        var encoding = Encoding.GetEncoding(codepage);
                        return encoding ?? Encoding.UTF8;
#endif
                    }
                    catch
                    {
                        // Fall back to UTF8 if everything goes wrong
                        return defaultEncoding;
                    }

                return defaultEncoding;
            }
        }





        // ReSharper disable InconsistentNaming
        const int MAX_DEFAULTCHAR = 2;
        const int MAX_LEADBYTES = 12;
        const int MAX_PATH = 260;

        // ReSharper disable MemberCanBePrivate.Local
        // ReSharper disable once StructCanBeMadeReadOnly
        [StructLayout(LayoutKind.Sequential)]
        struct CPINFOEX
        {
            [MarshalAs(UnmanagedType.U4)]
            public readonly int MaxCharSize;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_DEFAULTCHAR)]
            public readonly byte[] DefaultChar;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LEADBYTES)]
            public readonly byte[] LeadBytes;

            public readonly char UnicodeDefaultChar;

            [MarshalAs(UnmanagedType.U4)]
            public readonly int CodePage;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public readonly string CodePageName;
        }
        // ReSharper restore MemberCanBePrivate.Local
        // ReSharper restore InconsistentNaming
    }
}