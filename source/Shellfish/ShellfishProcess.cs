using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Octopus.Shellfish.Nix;
using Octopus.Shellfish.Windows;

namespace Octopus.Shellfish;

class ShellfishProcess : IDisposable
{
    static readonly IXPlatAdapter XPlatAdapter = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? new WindowsAdapter()
        : new NixAdapter();

    readonly Process process = new();
    readonly ShellCommandOptions? commandOptions;
    readonly Action<Process>? onCaptureProcess;

    bool stdOutRedirected;
    bool stdErrRedirected;

    InputState? inputState;

    SemaphoreSlim? exitedEvent;

    public ShellfishProcess(
        string executable,
        ShellCommandArguments arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables,
        NetworkCredential? credential,
        ShellCommandOptions? commandOptions,
        Action<Process>? onCaptureProcess,
        Encoding? outputEncoding,
        IReadOnlyCollection<IOutputTarget>? stdOutTargets,
        IReadOnlyCollection<IOutputTarget>? stdErrTargets,
        IInputSource? stdInSource)
    {
        this.commandOptions = commandOptions;
        this.onCaptureProcess = onCaptureProcess;
        process.StartInfo.FileName = executable;

        ConfigureArguments(arguments);

        if (workingDirectory is not null) process.StartInfo.WorkingDirectory = workingDirectory;

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        if (outputEncoding is not null)
        {
            process.StartInfo.StandardOutputEncoding = outputEncoding;
            process.StartInfo.StandardErrorEncoding = outputEncoding;
        }

        if (credential is not null)
        {
            XPlatAdapter.ConfigureStartInfoForUser(process.StartInfo, credential, environmentVariables);
        }
        else // exec as the current user
        {
            // Accessing the ProcessStartInfo.EnvironmentVariables dictionary will preload the environment variables for the current process
            // Then we'll add/overwrite with the customEnvironmentVariables           
            if (environmentVariables is { Count: > 0 })
            {
                foreach (var kvp in environmentVariables)
                {
                    process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }
        }

        ConfigureStdOut(stdOutTargets);
        ConfigureStdErr(stdErrTargets);
        ConfigureStdIn(stdInSource);
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        AttachProcessExitedEvent(cancellationToken);
        process.Start();

        onCaptureProcess?.Invoke(process);

        BeginIoStreams();
    }

    public void WaitForExit(CancellationToken cancellationToken = default)
    {
        try
        {
            exitedEvent?.Wait(cancellationToken);

            // Either: exitedEvent is null.
            // We can offload all the work to process.WaitForExit();
            //
            // Or: ExitEvent was signalled, but we still want to call process.WaitForExit; it waits for the StdErr and StdOut streams to flush.
            // in a way that can we cannot easily do ourselves.  https://github.com/microsoft/referencesource/blob/51cf7850defa8a17d815b4700b67116e3fa283c2/System/services/monitoring/system/diagnosticts/Process.cs#L2453 
            // It should return very quickly.
            if (!cancellationToken.IsCancellationRequested) process.WaitForExit(); // note we CANNOT pass a timeout here as otherwise the WaitForExit implementation will not wait for the streams to flush
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (stdOutRedirected) process.CancelOutputRead();
            if (stdErrRedirected) process.CancelErrorRead();

            // The legacy ShellExecutor would unconditionally kill the process upon cancellation.
            // We keep that default as it is the safest option for compatibility
            TryKillProcessAndChildrenRecursively();

            if (commandOptions != ShellCommandOptions.DoNotThrowOnCancellation) throw;
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await (exitedEvent?.WaitAsync(cancellationToken) ?? Task.CompletedTask);

            if (!cancellationToken.IsCancellationRequested)
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (stdOutRedirected) process.CancelOutputRead();
            if (stdErrRedirected) process.CancelErrorRead();

            // The legacy ShellExecutor would unconditionally kill the process upon cancellation.
            // We keep that default as it is the safest option for compatibility
            TryKillProcessAndChildrenRecursively();

            if (commandOptions != ShellCommandOptions.DoNotThrowOnCancellation) throw;
        }
    }

    public int SafelyGetExitCode()
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

     // Common code for Execute and ExecuteAsync to handle stdin and stdout streaming
    void BeginIoStreams()
    {
        if (stdOutRedirected) process.BeginOutputReadLine();
        if (stdErrRedirected) process.BeginErrorReadLine();

        inputState?.Begin(process.StandardInput);
    }

    void ConfigureArguments(ShellCommandArguments arguments)
    {
        switch (arguments)
        {
            case ShellCommandArguments.StringType s:
                process.StartInfo.Arguments = s.Value;
                break;
            case ShellCommandArguments.ArgumentListType { Values.Length: > 0 } l:
#if NET5_0_OR_GREATER
            // Prefer ArgumentList if we're on net5.0 or greater. Our polyfill should have the same behaviour, but
            // If we stick with the CLR we will pick up optimizations and bugfixes going forward           
            foreach (var arg in l.Values) process.StartInfo.ArgumentList.Add(arg);
#else
                process.StartInfo.Arguments = PasteArguments.JoinArguments(l.Values);
#endif
                break;
            
            // Deliberately no default case here: ShellCommandArguments.NoArgumentsType and Empty list are no-ops
        }
    }

    void ConfigureStdOut(IReadOnlyCollection<IOutputTarget>? targets)
    {
        if (targets is not { Count: > 0 }) return;

        process.StartInfo.RedirectStandardOutput = true;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return; // don't pass nulls along to the targets, it's an edge case that happens when the process exits

            foreach (var target in targets) target.WriteLine(e.Data);
        };
        stdOutRedirected = true;
    }

    void ConfigureStdErr(IReadOnlyCollection<IOutputTarget>? targets)
    {
        if (targets is not { Count: > 0 }) return;

        process.StartInfo.RedirectStandardError = true;
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return; // don't pass nulls along to the targets, it's an edge case that happens when the process exits

            foreach (var target in targets) target.WriteLine(e.Data);
        };
        stdErrRedirected = true;
    }

    void ConfigureStdIn(IInputSource? source)
    {
        if (source is null) return;

        process.StartInfo.RedirectStandardInput = true;
        inputState = new InputState(source);
    }

    void AttachProcessExitedEvent(CancellationToken cancellationToken)
    {
        // if the request isn't cancellable, we don't need to enable raising events or do any of this work
        if (cancellationToken == CancellationToken.None) return;

        exitedEvent = new SemaphoreSlim(0, 1);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            exitedEvent.Release();
        };
    }

    void TryKillProcessAndChildrenRecursively()
    {
        try
        {
            XPlatAdapter.TryKillProcessAndChildrenRecursively(process);
        }
        catch (Exception)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    public void Dispose()
    {
        inputState?.Dispose();
        process.Dispose();
    }

    class InputState(IInputSource source) : IDisposable
    {
        InputQueue? inputQueue;
        IDisposable? unsubscribeDisposable;

        public void Begin(StreamWriter writer)
        {
            inputQueue = new InputQueue(writer);
            unsubscribeDisposable = source.Subscribe(inputQueue);
        }

        public void Dispose()
        {
            inputQueue?.OnCompleted();
            unsubscribeDisposable?.Dispose();
        }
    }
}