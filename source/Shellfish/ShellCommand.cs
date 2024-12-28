using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Octopus.Shellfish;

/// <summary>
/// A fluent-builder style utility designed to easily let you execute shell commands.
/// </summary>
public class ShellCommand
{
    readonly string executable;
    List<string>? argumentList;
    string? argumentString;
    string? workingDirectory;
    IReadOnlyDictionary<string, string>? environmentVariables;
    NetworkCredential? windowsCredential;
    Encoding? outputEncoding;

    List<IOutputTarget>? stdOutTargets;
    List<IOutputTarget>? stdErrTargets;
    IInputSource? stdInSource;

    public ShellCommand(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) throw new ArgumentException("Executable must be a valid non-whitespace string.", nameof(executable));
        this.executable = executable;
    }

    /// <summary>
    /// Allows you to specify the working directory for the process.
    /// If you don't specify one, the process' Current Directory is used.
    /// </summary>
    /// <remarks>
    /// Internally ShellCommand sets UseShellExecute=false, so if unset "the current directory is understood to contain the executable."
    /// https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.workingdirectory?view=net-8.0
    /// </remarks>
    public ShellCommand WithWorkingDirectory(string workingDir)
    {
        workingDirectory = workingDir;
        return this;
    }

    /// <summary>
    /// Allows you to supply a list of individual string arguments.
    /// Arguments will be quoted and escaped if necessary, so you can freely pass arguments with spaces or special characters.
    /// </summary>
    public ShellCommand WithArguments(IEnumerable<string> argList)
    {
        argumentList ??= new List<string>();
        argumentList.Clear();
        argumentList.AddRange(argList);
        return this;
    }

    /// <summary>
    /// Allows you to supply a single argument string which will be used directly.
    /// No quoting or escaping of any spaces or special characters will be performed.
    /// </summary>
    public ShellCommand WithArguments(string argString)
    {
        argumentString = argString;
        return this;
    }

    /// <summary>
    /// Allows you to set additional environment variables for the launched process.
    /// </summary>
    /// <remarks>
    /// The C# IDictionary type does not implement IReadOnlyDictionary. If you have an IDictionary, you will
    /// need to copy it into something IReadOnlyDictionary compatible, such as new Dictionary&lt;string, string&gt;(yourDictionary).
    /// </remarks>
    public ShellCommand WithEnvironmentVariables(IReadOnlyDictionary<string, string> dictionary)
    {
        environmentVariables = dictionary;
        return this;
    }

    /// <summary>
    /// Runs the command as the given user.
    /// Currently only supported on Windows
    /// </summary>
    /// <param name="credential">The credential representing the user account.</param>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("Windows")]
#endif
    public ShellCommand WithCredentials(NetworkCredential credential)
    {
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

    /// <summary>
    /// Adds an output target for the standard output stream of the process.
    /// Typically, an extension method like WithStdOutTarget(StringBuilder) or WithStdOutTarget(Action&lt;string&gt;) would be used over this. 
    /// </summary>
    public ShellCommand WithStdOutTarget(IOutputTarget target)
    {
        stdOutTargets ??= new List<IOutputTarget>();
        stdOutTargets.Add(target);
        return this;
    }

    // Experimental: We are not 100% sure this is the right way to implement stdin
    internal ShellCommand WithStdInSource(IInputSource source)
    {
        stdInSource = source;
        return this;
    }

    /// <summary>
    /// Adds an output target for the standard error stream of the process.
    /// Typically, an extension method like WithStdErrTarget(StringBuilder) or WithStdErrTarget(Action&lt;string&gt;) would be used over this. 
    /// </summary>
    public ShellCommand WithStdErrTarget(IOutputTarget target)
    {
        stdErrTargets ??= new List<IOutputTarget>();
        stdErrTargets.Add(target);
        return this;
    }

    /// <summary>
    /// Launches the process and synchronously waits for it to exit.
    /// </summary>
    public ShellCommandResult Execute(CancellationToken cancellationToken = default)
    {
        using var process = new ShellfishProcess();
        process.Configure(
            executable,
            argumentString,
            argumentList,
            workingDirectory,
            environmentVariables,
            windowsCredential,
            outputEncoding,
            stdOutTargets,
            stdErrTargets,
            stdInSource);

        process.Start(cancellationToken);
        process.WaitForExit(cancellationToken);

        return new ShellCommandResult(process.SafelyGetExitCode());
    }

    /// <summary>
    /// Launches the process and asynchronously waits for it to exit.
    /// </summary>
    public async Task<ShellCommandResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var process = new ShellfishProcess();
        process.Configure(
            executable,
            argumentString,
            argumentList,
            workingDirectory,
            environmentVariables,
            windowsCredential,
            outputEncoding,
            stdOutTargets,
            stdErrTargets,
            stdInSource);

        process.Start(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ShellCommandResult(process.SafelyGetExitCode());
    }
}