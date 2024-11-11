using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Octopus.Shellfish;

public class DotnetRunCommand
{
    readonly ShellCommand shellCommand;
    readonly string? extraArgument;

    public DotnetRunCommand(string target)
    {
        var (executable, argument) = FigureItOut(target);

        extraArgument = argument;

        shellCommand = new ShellCommand(executable);
    }

    public DotnetRunCommand WithWorkingDirectory(string workingDirectory)
    {
        shellCommand.WithWorkingDirectory(workingDirectory);
        return this;
    }

    public DotnetRunCommand WithEnvironmentVariables(Dictionary<string, string> environmentVariables)
    {
        shellCommand.WithEnvironmentVariables(environmentVariables);
        return this;
    }

    public DotnetRunCommand WithArguments(string arguments)
    {
        if (!string.IsNullOrEmpty(extraArgument))
        {
            arguments = string.Join(" ", [extraArgument, ..arguments]);
        }
        shellCommand.WithArguments(arguments);
        return this;
    }

#if NET5_0_OR_GREATER
    [SupportedOSPlatform("Windows")]
#endif
    public DotnetRunCommand WithCredentials(NetworkCredential credential)
    {
        shellCommand.WithCredentials(credential);
        return this;
    }

    public ShellCommandResult Execute(CancellationToken cancellationToken = default)
    {
        return shellCommand.Execute(cancellationToken);
    }

    public async Task<ShellCommandResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return await shellCommand.ExecuteAsync(cancellationToken);
    }

    static (string executable, string? argument) FigureItOut(string target)
    {
        return Path.GetExtension(target).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            ? ("dotnet", target)
            : (target, null);
    }
}