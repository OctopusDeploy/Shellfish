using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Octopus.Shellfish;

using static ShellCommandExecutorHelpers;

// This is the NEW shellfish API. It is currently under development
public class ShellCommand(string executable)
{
    readonly string executable = executable;
    List<string>? commandLineArguments;
    string? rawCommandLineArguments;
    string? workingDirectory;
    Dictionary<string, string>? environmentVariables;
    NetworkCredential? windowsCredential;
    Encoding? outputEncoding;
    List<Action<Process>>? beforeStartHooks;

    // The legacy ShellExecutor would unconditionally kill the process upon cancellation.
    // We keep that default as it is the safest option for compaitbility, but it can be changed
    bool shouldKillProcessOnCancellation = true;

    // The legacy ShellExecutor would not throw an OperationCanceledException if CancellationToken was signaled.
    // This is a bit weird and not standard for .NET, but we keep it as the default for compatibility.
    bool shouldSwallowCancellationException = true;

    List<IOutputTarget>? stdOutTargets;
    List<IOutputTarget>? stdErrTargets;

    public ShellCommand WithWorkingDirectory(string workingDir)
    {
        workingDirectory = workingDir;
        return this;
    }

    public ShellCommand WithArguments(params string[] arguments)
    {
        commandLineArguments ??= new List<string>();
        commandLineArguments.Clear();
        commandLineArguments.AddRange(arguments);
        return this;
    }
    
    /// <summary>
    /// This allows you to set a callback which can inspect and modify the process before it is started.
    /// You can use it for advanced use-cases or to build extensions on top of ShellCommand
    /// </summary>
    public ShellCommand BeforeStartHook(Action<Process> hook)
    {
        beforeStartHooks ??= new List<Action<Process>>();
        beforeStartHooks.Add(hook);
        return this;
    }

    /// <summary>
    /// Allows you to supply a string which will be passed directly to Process.StartInfo.Arguments,
    /// can be useful if you have custom quoting requirements or other special needs.
    /// </summary>
    public ShellCommand WithRawArguments(string rawArguments)
    {
        rawCommandLineArguments = rawArguments;
        return this;
    }
    
    public ShellCommand WithEnvironmentVariables(Dictionary<string, string> dictionary)
    {
        environmentVariables = dictionary;
        return this;
    }
    
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("Windows")]
#endif
    public ShellCommand RunAsUser(NetworkCredential credential)
    {
        // Note: "RunAsUser" name is generic because we could expand this to support unix in future. Right now it's just windows.
        windowsCredential = credential;
        return this;
    }

    /// <summary>
    /// This controls the value of process.StartInfo.StandardOutputEncoding and process.StartInfo.StandardErrorEncoding.
    /// It can be used if you are running a process that outputs text in a nonstandard encoding that System.Diagnostics.Process
    /// can't handle automatically
    /// </summary>
    /// <remarks>
    /// The old ShellExecutor API would always set the Output Encoding on windows to the "OEM" encoding using
    /// XPlatAdapter.GetOemEncoding() to determine what that was.
    ///
    /// This traces back to an old bug (in powershell, possibly?), which I could not reproduce on a modern Windows 11 system.
    /// As it does not appear to be required anymore, we no longer set the OEM encoding by default, but this lets you set it manually, should you have some other reason.
    ///   https://github.com/OctopusDeploy/Issues/issues/748
    ///   https://github.com/OctopusDeploy/OctopusDeploy/commit/a223657f4d5d64bde8a638322d3f6f2f7b188169
    /// </remarks>
    public ShellCommand WithOutputEncoding(Encoding encoding)
    {
        outputEncoding = encoding;
        return this;
    }
    
    public ShellCommand CaptureStdOutTo(StringBuilder stringBuilder)
    {
        stdOutTargets ??= new List<IOutputTarget>();
        stdOutTargets.Add(new CapturedStringBuilderTarget(stringBuilder));
        return this;
    }

    public ShellCommand CaptureStdErrTo(StringBuilder stringBuilder)
    {
        stdErrTargets ??= new List<IOutputTarget>();
        stdErrTargets.Add(new CapturedStringBuilderTarget(stringBuilder));
        return this;
    }

    public ShellCommand KillProcessOnCancellation(bool shouldKill = true)
    {
        shouldKillProcessOnCancellation = shouldKill;
        return this;
    }

    public ShellCommand SwallowCancellationException(bool shouldSwallow = true)
    {
        shouldSwallowCancellationException = shouldSwallow;
        return this;
    }

    // sets standard flags on the Process that apply in all cases
    void ConfigureProcess(Process process, out bool shouldBeginOutputRead, out bool shouldBeginErrorRead)
    {
        process.StartInfo.FileName = executable!;

        if (rawCommandLineArguments is not null && commandLineArguments is { Count: > 0 }) throw new InvalidOperationException("Cannot specify both raw arguments and arguments");

        if (rawCommandLineArguments is not null)
        {
            process.StartInfo.Arguments = rawCommandLineArguments;
        }
        else if (commandLineArguments is { Count: > 0 })
        {
#if NET5_0_OR_GREATER
            // Prefer ArgumentList if we're on net5.0 or greater. Our polyfill should have the same behaviour, but
            // If we stick with the CLR we will pick up optimizations and bugfixes going forward
            foreach (var arg in commandLineArguments) process.StartInfo.ArgumentList.Add(arg);
#else
            process.StartInfo.Arguments = PasteArguments.JoinArguments(commandLineArguments);
#endif
        }

        if (workingDirectory is not null) process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        if (outputEncoding is not null)
        {
            process.StartInfo.StandardOutputEncoding = outputEncoding;
            process.StartInfo.StandardErrorEncoding = outputEncoding;
        }

        if (windowsCredential is not null)
        {
            XPlatAdapter.ConfigureStartInfoForUser(process.StartInfo, windowsCredential, environmentVariables);
        }
        else // exec as the current user
        {
            // Accessing the ProcessStartInfo.EnvironmentVariables dictionary will pre-load the environment variables for the current process
            // Then we'll add/overwrite with the customEnvironmentVariables
            if (environmentVariables is { Count: > 0 })
            {
                foreach (var kvp in environmentVariables)
                {
                    process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }
        }

        shouldBeginOutputRead = shouldBeginErrorRead = false;
        if (stdOutTargets is { Count: > 0 })
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
        
        beforeStartHooks?.ForEach(hook => hook(process));
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

            if (shouldKillProcessOnCancellation) TryKillProcessAndChildrenRecursively(process);
            if (!shouldSwallowCancellationException) throw;
        }

        return new ShellCommandResult(SafelyGetExitCode(process));
    }

    public async Task<ShellCommandResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("No executable specified");

        var process = new Process();
        ConfigureProcess(process, out var shouldBeginOutputRead, out var shouldBeginErrorRead);
        
        beforeStartHooks?.ForEach(hook => hook(process));
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

            if (shouldKillProcessOnCancellation) TryKillProcessAndChildrenRecursively(process);
            if (!shouldSwallowCancellationException) throw;
        }

        return new ShellCommandResult(SafelyGetExitCode(process));
    }

    interface IOutputTarget
    {
        void DataReceived(string? line);
    }

    class CapturedStringBuilderTarget(StringBuilder stringBuilder) : IOutputTarget
    {
        readonly StringBuilder stringBuilder = stringBuilder;
        
        public void DataReceived(string? line) => stringBuilder.AppendLine(line);
    }
}