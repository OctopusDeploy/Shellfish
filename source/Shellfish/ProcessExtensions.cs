using System;
#if !NET5_0_OR_GREATER
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Octopus.Shellfish;

static class ProcessExtensions
{
    // WaitForExitAsync was added in net5. It handles the buffer flushing scenario so we can simply call it.
    public static async Task WaitForExitAsync(this Process process, CancellationToken cancellationToken)
    {
        // Compatibility shim for netstandard2.0
        // Similarly to the sync version, We want to wait for the StdErr and StdOut streams to flush but cannot easily
        // do this ourselves. https://github.com/dotnet/runtime/blob/e03b9a4692a15eb3ffbb637439241e8f8e5ca95f/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.cs#L1565
        await Task.CompletedTask;
        process.WaitForExit(); // note we CANNOT pass a timeout here as otherwise the WaitForExit implementation will not wait for the streams to flush
    }
}
#endif