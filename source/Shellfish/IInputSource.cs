using System;

namespace Octopus.Shellfish;

public interface IInputSource
{
    IDisposable Subscribe(Action<string> onNext);
}