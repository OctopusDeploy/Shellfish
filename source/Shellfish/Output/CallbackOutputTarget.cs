using System;

namespace Octopus.Shellfish.Output;

public class CallbackOutputTarget(Action<string?> callback) : IOutputTarget
{
    public void WriteLine(string? line) => callback(line);
}