using System;

namespace Octopus.Shellfish.Output;

public static class CallbackOutputTargetExtensions
{
    public static ShellCommand WithStdOutTarget(this ShellCommand command, Action<string?> callback)
        => command.WithStdOutTarget(new CallbackOutputTarget(callback));

    public static ShellCommand WithStdErrTarget(this ShellCommand command, Action<string?> callback)
        => command.WithStdErrTarget(new CallbackOutputTarget(callback));
}