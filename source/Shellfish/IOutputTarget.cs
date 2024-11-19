using System;

namespace Octopus.Shellfish;

public interface IOutputTarget
{
    void WriteLine(string line);
}