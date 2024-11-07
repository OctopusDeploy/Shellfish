using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

namespace Tests;

public class ShellCommandExtensionMethodsFixture
{
    readonly CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(45));
    CancellationToken CancellationToken => cancellationTokenSource.Token;

    // The trick with these tests is that we don't need to actually execute anything, we just need to check
    // that the hook made the right modifications to the Process.
    class StopAndCaptureProcessException(Process process) : Exception
    {
        public Process Process { get; } = process;
    }
    
    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ExecutableCanBeDotnetDll_LeavesExesAlone(SyncBehaviour behaviour)
    {
        var executor = new ShellCommand("foo.exe")
            .WithRawArguments("arg1 arg2")
            .ExecutableCanBeDotnetDll()
            .BeforeStartHook(process => throw new StopAndCaptureProcessException(process));

        try
        {
            _ = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);
            
            throw new Exception("Should not get here");
        }
        catch (StopAndCaptureProcessException e)
        {
            var startInfo = e.Process.StartInfo;
            startInfo.FileName.Should().Be("foo.exe");
            startInfo.Arguments.Should().Be("arg1 arg2");
        }
    }
    
    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ExecutableCanBeDotnetDll_RedirectsDllToDotnet_WithRawArguments(SyncBehaviour behaviour)
    {
        var executor = new ShellCommand("foo.dll")
            .WithRawArguments("arg1 arg2")
            .ExecutableCanBeDotnetDll()
            .BeforeStartHook(process => throw new StopAndCaptureProcessException(process));

        try
        {
            _ = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);
            
            throw new Exception("Should not get here");
        }
        catch (StopAndCaptureProcessException e)
        {
            var startInfo = e.Process.StartInfo;
            startInfo.FileName.Should().Be("dotnet");
            startInfo.Arguments.Should().Be("foo.dll arg1 arg2"); // deliberate quoting in case the executable has spaces
        }
    }
    
    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ExecutableCanBeDotnetDll_RedirectsDllToDotnet_WithArgumentList(SyncBehaviour behaviour)
    {
        var executor = new ShellCommand("foo.dll")
            .WithArguments("arg1", "arg2")
            .ExecutableCanBeDotnetDll()
            .BeforeStartHook(process => throw new StopAndCaptureProcessException(process));

        try
        {
            _ = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);
            
            throw new Exception("Should not get here");
        }
        catch (StopAndCaptureProcessException e)
        {
            var startInfo = e.Process.StartInfo;
            startInfo.FileName.Should().Be("dotnet");
#if NET5_0_OR_GREATER
            startInfo.ArgumentList.Should().Equal("foo.dll", "arg1", "arg2");
#else // our compatibility shim produces raw arguments for .NET Framework
            startInfo.Arguments.Should().Be("foo.dll arg1 arg2");
#endif
        }
    }
    
    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ExecutableCanBeDotnetDll_QuotesExecutableIfNeeded(SyncBehaviour behaviour)
    {
        var executor = new ShellCommand("C:\\Program Files\\My Stuff\\foo.dll")
            .WithRawArguments("arg1 arg2")
            .ExecutableCanBeDotnetDll()
            .BeforeStartHook(process => throw new StopAndCaptureProcessException(process));

        try
        {
            _ = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);
            
            throw new Exception("Should not get here");
        }
        catch (StopAndCaptureProcessException e)
        {
            var startInfo = e.Process.StartInfo;
            startInfo.FileName.Should().Be("dotnet");
            startInfo.Arguments.Should().Be("\"C:\\Program Files\\My Stuff\\foo.dll\" arg1 arg2");
        }
    }
}