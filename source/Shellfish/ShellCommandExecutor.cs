using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Octopus.Shellfish;

// This is the NEW shellfish API. It is currently under development
public class ShellCommandExecutor
{
    string? executable;
    string? commandLinePrefixArgument; // special case to allow WithDotNetExecutable to work
    List<string>? commandLineArguments;
    string? rawCommandLineArguments;
    string? workingDirectory;

    List<IOutputTarget>? stdOutTargets;
    List<IOutputTarget>? stdErrTargets;

    public ShellCommandExecutor WithExecutable(string exe)
    {
        executable = exe;
        return this;
    }

    public ShellCommandExecutor WithWorkingDirectory(string workingDir)
    {
        workingDirectory = workingDir;
        return this;
    }

    // Configures the runner to launch the specified executable if it is a .exe, or to launch the specified .dll using dotnet.exe if it is a .dll.
    // assumes "dotnet" is in the PATH somewhere
    public ShellCommandExecutor WithDotNetExecutable(string exeOrDll)
    {
        if (exeOrDll.EndsWith(".dll"))
        {
            commandLinePrefixArgument = exeOrDll;
            executable = "dotnet";
        }

        return this;
    }

    public ShellCommandExecutor WithArguments(params string[] arguments)
    {
        commandLineArguments ??= new List<string>();
        commandLineArguments.Clear();
        commandLineArguments.AddRange(arguments);
        return this;
    }

    /// <summary>
    /// Allows you to supply a string which will be passed directly to Process.StartInfo.Arguments,
    /// can be useful if you have custom quoting requirements or other special needs.
    /// </summary>
    public ShellCommandExecutor WithRawArguments(string rawArguments)
    {
        rawCommandLineArguments = rawArguments;
        return this;
    }

    public ShellCommandExecutor CapturingStdOut()
    {
        stdOutTargets ??= new List<IOutputTarget>();
        if (stdOutTargets.Any(t => t is CapturedStringBuilderTarget)) return this; // already capturing
        stdOutTargets.Add(new CapturedStringBuilderTarget());
        return this;
    }

    public ShellCommandExecutor CapturingStdErr()
    {
        stdErrTargets ??= new List<IOutputTarget>();
        if (stdErrTargets.Any(t => t is CapturedStringBuilderTarget)) return this; // already capturing
        stdErrTargets.Add(new CapturedStringBuilderTarget());
        return this;
    }

    // sets standard flags on the Process that apply in all cases
    void ConfigureProcess(Process process, out bool shouldBeginOutputRead, out bool shouldBeginErrorRead)
    {
        process.StartInfo.FileName = executable!;

        if (rawCommandLineArguments is not null && commandLineArguments is { Count: > 0 }) throw new InvalidOperationException("Cannot specify both raw arguments and arguments");

        if (rawCommandLineArguments is not null)
        {
            process.StartInfo.Arguments = commandLinePrefixArgument is not null 
                ? $"{commandLinePrefixArgument} {rawCommandLineArguments}"
                : rawCommandLineArguments;
        }
        else if (commandLineArguments is { Count: > 0 })
        {
#if NET5_0_OR_GREATER
            // Prefer ArgumentList if we're on net5.0 or greater. Our polyfill should have the same behaviour, but
            // If we stick with the CLR we will pick up optimizations and bugfixes going forward
            if (commandLinePrefixArgument is not null) process.StartInfo.ArgumentList.Add(commandLinePrefixArgument);

            foreach (var arg in commandLineArguments) process.StartInfo.ArgumentList.Add(arg);
#else
            var fullArgs = commandLinePrefixArgument is not null
                ? [commandLinePrefixArgument, ..commandLineArguments]
                : commandLineArguments;

            process.StartInfo.Arguments = PasteArguments.JoinArguments(fullArgs);
#endif
        }
        else if (commandLinePrefixArgument is not null)
        {
            // e.g. WithDotNetExecutable("foo.dll") with no other args; and we need to do "dotnet foo.dll"
            process.StartInfo.Arguments = commandLinePrefixArgument;
        }

        if (workingDirectory is not null) process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        shouldBeginOutputRead = shouldBeginErrorRead = false;
        if (stdOutTargets is
            {
                Count: > 0
            })
        {
            process.StartInfo.RedirectStandardOutput = true;
            shouldBeginOutputRead = true;

            var targets = stdOutTargets.ToArray();
            process.OutputDataReceived += (_, e) =>
            {
                foreach (var target in targets)
                {
                    target.DataReceived(e.Data);
                }
            };
        }

        if (stdErrTargets is { Count: > 0 })
        {
            process.StartInfo.RedirectStandardError = true;
            shouldBeginErrorRead = true;

            var targets = stdErrTargets.ToArray();
            process.ErrorDataReceived += (_, e) =>
            {
                foreach (var target in targets)
                {
                    target.DataReceived(e.Data);
                }
            };
        }
    }

    public ShellCommandResult Execute(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("No executable specified");

        var process = new Process();
        ConfigureProcess(process, out var shouldBeginOutputRead, out var shouldBeginErrorRead);

        process.Start();

        if (shouldBeginOutputRead) process.BeginOutputReadLine();
        if (shouldBeginErrorRead) process.BeginErrorReadLine();

        try
        {
            if (cancellationToken == default)
            {
                process.WaitForExit();
            }
            else // cancellation is hard
            {
                WaitForExitInNewThread(process, cancellationToken).GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (shouldBeginOutputRead) process.CancelOutputRead();
            if (shouldBeginErrorRead) process.CancelErrorRead();
            throw;
        }

        return new ShellCommandResult(
            SafelyGetExitCode(process),
            stdOutTargets?.OfType<CapturedStringBuilderTarget>().FirstOrDefault()?.StringBuilder,
            stdErrTargets?.OfType<CapturedStringBuilderTarget>().FirstOrDefault()?.StringBuilder);
    }

    public async Task<ShellCommandResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("No executable specified");

        var process = new Process();
        ConfigureProcess(process, out var shouldBeginOutputRead, out var shouldBeginErrorRead);
        process.Start();

        if (shouldBeginOutputRead) process.BeginOutputReadLine();
        if (shouldBeginErrorRead) process.BeginErrorReadLine();

        try
        {
#if NET5_0_OR_GREATER // WaitForExitAsync was added in net5; we can't use it when targeting netstandard2.0 
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#else // fake it out.
            await WaitForExitInNewThread(process, cancellationToken).ConfigureAwait(false);
#endif
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (shouldBeginOutputRead) process.CancelOutputRead();
            if (shouldBeginErrorRead) process.CancelErrorRead();
            throw;
        }

        return new ShellCommandResult(
            SafelyGetExitCode(process),
            stdOutTargets?.OfType<CapturedStringBuilderTarget>().FirstOrDefault()?.StringBuilder,
            stdErrTargets?.OfType<CapturedStringBuilderTarget>().FirstOrDefault()?.StringBuilder);
    }

    static int SafelyGetExitCode(Process process)
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
    
    static Task WaitForExitInNewThread(Process process, CancellationToken cancellationToken)
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

    interface IOutputTarget
    {
        void DataReceived(string? line);
    }

    class CapturedStringBuilderTarget : IOutputTarget
    {
        public void DataReceived(string? line) => StringBuilder.AppendLine(line);

        public StringBuilder StringBuilder { get; } = new();
    }
}