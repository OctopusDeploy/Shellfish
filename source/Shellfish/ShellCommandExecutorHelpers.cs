using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Octopus.Shellfish.Nix;
using Octopus.Shellfish.Windows;

namespace Octopus.Shellfish;

public static class ShellCommandExecutorHelpers
{
    static readonly IXPlatAdapter XPlatAdapter = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? new WindowsAdapter()
        : new NixAdapter();
    
    internal static int SafelyGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException ex)
            when (ex.Message is "No process is associated with this object." or "Process was not started by this object, so requested information cannot be determined.")
        {
            return -1;
        }
    }
    
    internal static void TryKillProcessAndChildrenRecursively(Process process, Action<string>? error = null)
    {
        try
        {
            XPlatAdapter.TryKillProcessAndChildrenRecursively(process);
        }
        catch (Exception exception)
        {
            error?.Invoke($"Failed to kill the launched process and its children: {exception}");
            try
            {
                process.Kill();
            }
            catch (Exception exceptionAfterRetry)
            {
                error?.Invoke($"Failed to kill the launched process: {exceptionAfterRetry}");
            }
        }
    }

    internal static Task WaitForExitInNewThread(Process process, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        CancellationTokenRegistration registration = default;
        registration = cancellationToken.Register(() =>
        {
            registration.Dispose();
            tcs.TrySetCanceled();
        });

        new Thread(() =>
        {
            try
            {
                process.WaitForExit();
                tcs.TrySetResult(true);
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        }).Start();
        return tcs.Task;
    }
}