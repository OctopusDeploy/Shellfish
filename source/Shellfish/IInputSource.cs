using System;

namespace Octopus.Shellfish;

// Experimental: We are not 100% sure this is the right way to implement stdin
internal interface IInputSource
{
    IDisposable Subscribe(IInputSourceObserver observer);
}

// Experimental: We are not 100% sure this is the right way to implement stdin
internal interface IInputSourceObserver
{
    void OnNext(string line);
    void OnCompleted();
}