using System.Collections.Generic;

namespace Octopus.Shellfish;

class StringInputSource(string value) : IInputSource
{
    public IEnumerable<string> GetInput() => [value];
}

public static class StringInputSourceExtensions
{
    public static ShellCommand WithStdInSource(this ShellCommand shellCommand, string input)
    {
        shellCommand.WithStdInSource(new StringInputSource(input));
        return shellCommand;
    }
}