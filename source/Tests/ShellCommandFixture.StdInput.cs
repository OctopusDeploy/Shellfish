using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

namespace Tests;

// Cross-platform tests for ShellCommand which deal with std input
public class ShellCommandFixtureStdInput
{
    readonly CancellationTokenSource cancellationTokenSource = new(ShellCommandFixture.TestTimeout);
    CancellationToken CancellationToken => cancellationTokenSource.Token;
    
    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ShouldWork(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            cmd: """
                 @echo off
                 echo Enter First Name:
                 set /p firstname=
                 echo Hello %firstname%
                 """,
            sh: """
                echo "Enter First Name:"
                read firstname
                echo "Hello $firstname"
                """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        // it's going to ask us for the names, we need to answer back or the process will stall forever; we can preload this
        var stdIn = new BufferedInputSource("Bob");

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource(stdIn)
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Hello Bob" + Environment.NewLine);
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task MultipleInputItemsShouldWork(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            cmd: """
                 @echo off
                 echo Enter First Name:
                 set /p firstname=
                 echo Enter Last Name:
                 set /p lastname=
                 echo Hello %firstname% %lastname%
                 """,
            sh: """
                echo "Enter First Name:"
                read firstname
                echo "Enter Last Name:"
                read lastname
                echo "Hello $firstname $lastname"
                """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        
        // it's going to ask us for the names, we need to answer back or the process will stall forever; we can preload this
        var stdIn = new TestInputSource();

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource(stdIn)
            .WithStdOutTarget(stdOut)
            .WithStdOutTarget(l =>
            {
                if (l.Contains("Enter First Name:")) stdIn.OnNext("Bob");
                if (l.Contains("Enter Last Name:")) stdIn.OnNext("Octopus");
            })
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Enter Last Name:" + Environment.NewLine + "Hello Bob Octopus" + Environment.NewLine);
    }
    
    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ShouldReleaseInputSourceWhenProgramExits(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            cmd: """
                 @echo off
                 echo Enter First Name:
                 set /p firstname=
                 echo Hello %firstname%
                 """,
            sh: """
                echo "Enter First Name:"
                read firstname
                echo "Hello $firstname"
                """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        // it's going to ask us for the names, we need to answer back or the process will stall forever; we can preload this
        var stdIn = new TestInputSource();

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource(stdIn)
            .WithStdOutTarget(stdOut)
            .WithStdOutTarget(l =>
            {
                stdIn.Subscriber.Should().NotBeNull("the shellcommand should still be subscribed to the input source while the process is running");
                
                // when we receive the first prompt, cancel and kill the process
                if (l.Contains("Enter First Name:")) stdIn.OnNext("Bob");
            })
            .WithStdErrTarget(stdErr);

        stdIn.Subscriber.Should().BeNull("the shellcommand should not subscribe to the input source until the process starts");
        
        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Hello Bob" + Environment.NewLine);
        
        stdIn.Subscriber.Should().BeNull("the shellcommand should have unsubscribed from the input source after the process exits");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ShouldBeCancellable(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            cmd: """
                 @echo off
                 echo Enter Name:
                 set /p name=
                 echo Hello %name%
                 """,
            sh: """
                echo "Enter Name:"
                read name
                echo "Hello $name"
                """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        // it's going to ask us for the name first, but we don't give it anything; the script should hang
        var stdIn = new TestInputSource();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource(stdIn)
            .WithStdOutTarget(stdOut)
            .WithStdOutTarget(l =>
            {
                // when we receive the first prompt, cancel and kill the process
                if (l.Contains("Enter Name:")) cts.Cancel();
            })
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(cts.Token)
            : executor.Execute(cts.Token);

        // Our process was waiting on stdin and exits itself with code 0 when we close stdin,
        // but we cannot 100% guarantee it shuts down in time before we proceed to killing it; we could observe -1 too.
        // Whenever I've run this locally on windows or linux I always observe 0.
        result.ExitCode.Should().BeOneOf([0, -1], "The process should exit cleanly when stdin is closed, but we might kill depending on timing");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter Name:" + Environment.NewLine);
    }
    
    // If someone wants to have an interactive back-and-forth with a process, they 
    // can use a type like this to do it. We don't want to quite commit to putting it
    // in the public API though until we have a stronger use-case for it.
    class TestInputSource : IInputSource, IDisposable
    {
        // make the subscriber public so tests can verify it
        public Action<string>? Subscriber { get; private set; }

        public IDisposable Subscribe(Action<string> onNext)
        {
            if (Subscriber != null) throw new InvalidOperationException("Only one subscriber is allowed");
            Subscriber = onNext;
            return this;
        }

        public void OnNext(string line)
        {
            Subscriber?.Invoke(line);
        }

        public void Dispose()
        {
            Subscriber = null;
        }
    }
}