using System;

namespace Octopus.Shellfish.Output;

public interface IOutputTarget
{
    void WriteLine(string? line);
}