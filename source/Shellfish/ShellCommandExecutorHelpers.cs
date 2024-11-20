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
    internal static readonly IXPlatAdapter XPlatAdapter = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
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

    internal static ManualResetEventSlim? AttachProcessExitedManualResetEvent(Process process, CancellationToken cancellationToken)
    {
        // if the request isn't cancellable, we don't need to enable raising events or do any of this work
        if (cancellationToken == default) return null;

        var mre = new ManualResetEventSlim(false);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            mre.Set();
        };
        return mre;
    }

    internal static Task AttachProcessExitedTask(Process process, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        var tokenRegistration = cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled();
        });

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            tokenRegistration.Dispose();
            tcs.TrySetResult(true);
        };
        return tcs.Task;
    }
}