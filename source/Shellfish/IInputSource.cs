using System;

namespace Octopus.Shellfish;

public interface IInputSource
{
    IDisposable Subscribe(IInputSourceObserver observer);
}

public interface IInputSourceObserver
{
    void OnNext(string line);
    void OnCompleted();
}