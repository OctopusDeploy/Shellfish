using System.Collections.Generic;

namespace Octopus.Shellfish;

public interface IInputSource
{
    IEnumerable<string> GetInput();
}