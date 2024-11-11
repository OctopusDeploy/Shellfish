using System;
using System.Text;

namespace Octopus.Shellfish.Output;

public static class StringBuilderTargetExtensions
{
    public static ShellCommand WithStdOutTarget(this ShellCommand command, StringBuilder builder)
        => command.WithStdOutTarget(new StringBuilderOutputTarget(builder));

    public static ShellCommand WithStdErrTarget(this ShellCommand command, StringBuilder builder)
        => command.WithStdErrTarget(new StringBuilderOutputTarget(builder));
}