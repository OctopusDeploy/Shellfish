using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

namespace Tests;

public class ShellCommandFixtureStdIn
{
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

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource("Bob") // it's going to ask us for the names, we need to answer back or the process will stall forever; we can preload this
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken.None)
            : executor.Execute(CancellationToken.None);

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
                if (l.Contains("First")) stdIn.AppendLine("Bob");
                if (l.Contains("Last"))
                {
                    stdIn.AppendLine("Octopus");
                    stdIn.Complete();
                }
            })
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken.None)
            : executor.Execute(CancellationToken.None);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Enter Last Name:" + Environment.NewLine + "Hello Bob Octopus" + Environment.NewLine);
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ClosingStdInEarly(SyncBehaviour behaviour)
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
                if (l.Contains("First")) stdIn.AppendLine("Bob");
                if (l.Contains("Last")) stdIn.Complete(); // shut it down
            })
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken.None)
            : executor.Execute(CancellationToken.None);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        // When we close stdin the waiting process receives an EOF; Our trivial shell script interprets this as an empty string
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Enter Last Name:" + Environment.NewLine + "Hello Bob " + Environment.NewLine);
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

        using var cts = new CancellationTokenSource();
        // it's going to ask us for the name first, but we don't give it anything; the script should hang
        var stdIn = new TestInputSource(cts.Token);

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
}

public class TestInputSource(CancellationToken? cancellationToken = null) : IInputSource
{
    readonly BlockingCollection<string> collection = new();

    public void AppendLine(string line)
    {
        collection.Add(line + Environment.NewLine);
    }

    public void Complete()
    {
        collection.CompleteAdding();
    }

    public IEnumerable<string> GetInput() => collection.GetConsumingEnumerable(cancellationToken ?? CancellationToken.None);
}