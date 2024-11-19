﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

// We disable this because we want to test the synchronous version of the method as well as the async version using the same test method
// ReSharper disable MethodHasAsyncOverload

namespace Tests;

// Cross-platform tests for ShellCommandExecutor
public class ShellCommandFixture
{
    // ReSharper disable InconsistentNaming
    const int SIG_TERM = 143;
    const int SIG_KILL = 137;

    static readonly string Command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash";
    static readonly string CommandParam = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c" : "-c";

    // Mimic the cancellation behaviour from LoggedTest in Octopus Server; we can't reference it in this assembly
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(45);

    readonly CancellationTokenSource cancellationTokenSource = new(TestTimeout);
    CancellationToken CancellationToken => cancellationTokenSource.Token;

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ExitCode_ShouldBeReturned(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand(Command)
            .WithArguments($"{CommandParam} \"exit 99\"")
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(99, "our custom exit code should be reflected");

        // we're executing cmd.exe which writes a newline to stdout and stderr
        stdOut.ToString().Should().BeEmpty("no messages should be written to stdout");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task RunningAsSameUser_ShouldCopySpecialEnvironmentVariables(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand(Command)
            .WithArguments($"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"")
            .WithEnvironmentVariables(new Dictionary<string, string>
            {
                { "customenvironmentvariable", "customvalue" }
            })
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty(Environment.NewLine, "no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task CancellationToken_ShouldForceKillTheProcess(SyncBehaviour behaviour)
    {
        // Terminate the process after a very short time so the test doesn't run forever
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        // Starting a new instance of cmd.exe will run indefinitely waiting for user input
        var executor = new ShellCommand(Command)
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(cts.Token)
            : executor.Execute(cts.Token);

        var exitCode = result.ExitCode;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            exitCode.Should().BeLessOrEqualTo(0, "the process should have been terminated");
            stdOut.ToString().Should().ContainEquivalentOf("Microsoft Windows", "the default command-line header would be written to stdout");
        }
        else
        {
            exitCode.Should().BeOneOf(SIG_KILL, SIG_TERM, 0, -1);
        }

        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr, and the process was terminated before the trailing newline got there");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task EchoHello_ShouldWriteToCapturedStdOutStringBuilder(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand(Command)
            .WithArguments($"{CommandParam} \"echo hello\"")
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf("hello");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task EchoHello_ShouldWriteToCapturedStdOutCallback(SyncBehaviour behaviour)
    {
        var outMessages = new List<string>();
        var errMessages = new List<string>();

        var executor = new ShellCommand(Command)
            .WithArguments($"{CommandParam} \"echo hello\"")
            .WithStdOutTarget(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) outMessages.Add(line);
            })
            .WithStdErrTarget(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) errMessages.Add(line);
            });

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        errMessages.Should().BeEmpty("no messages should be written to stderr");
        outMessages.Should().ContainSingle(msg => msg.Contains("hello"));
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task EchoError_ShouldWriteToCapturedStdErrCallback(SyncBehaviour behaviour)
    {
        var outMessages = new List<string>();
        var errMessages = new List<string>();

        var executor = new ShellCommand(Command)
            .WithArguments($"{CommandParam} \"echo Something went wrong! 1>&2\"")
            .WithStdOutTarget(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) outMessages.Add(line);
            })
            .WithStdErrTarget(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) errMessages.Add(line);
            });

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        outMessages.Should().BeEmpty("no messages should be written to stdout");
        errMessages.Should().ContainSingle(msg => msg.Contains("Something went wrong!"));
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task MultipleCapturingCallbacks(SyncBehaviour behaviour)
    {
        var outStringBuilder = new StringBuilder();
        var outStringBuilder2 = new StringBuilder();
        var outMessages = new List<string>();

        var executor = new ShellCommand(Command)
            .WithArguments($"{CommandParam} \"echo hello&& echo goodbye\"")
            .WithStdOutTarget(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) outMessages.Add($"FirstHook:{line}");
            })
            .WithStdOutTarget(outStringBuilder)
            .WithStdOutTarget(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) outMessages.Add($"SecondHook:{line}");
            })
            .WithStdOutTarget(outStringBuilder2);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        outMessages.Should().Equal("FirstHook:hello", "SecondHook:hello", "FirstHook:goodbye", "SecondHook:goodbye");
        outStringBuilder.ToString().Should().Be("hello" + Environment.NewLine + "goodbye" + Environment.NewLine);
        outStringBuilder2.ToString().Should().Be("hello" + Environment.NewLine + "goodbye" + Environment.NewLine);
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task RunAsCurrentUser_ShouldWork(SyncBehaviour behaviour)
    {
        var arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{CommandParam} \"echo {EchoEnvironmentVariable("username")}\""
            : $"{CommandParam} \"whoami\"";

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand(Command)
            .WithArguments(arguments)
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf($@"{Environment.UserName}");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ArgumentArrayHandlingShouldBeConsistentWithRawArgumsnts(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand(Command)
            .WithArguments([CommandParam, "echo hello"])
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf("hello");
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task ArgumentArrayHandlingShouldBeCorrect(SyncBehaviour behaviour)
    {
        using var assertionScope = new AssertionScope();
        var tempScript = CreateScriptWhichEchoesBackArguments();
        try
        {
            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

            string[] inputArgs =
            [
                "apple",
                "banana split",
                "--thing=\"quotedValue\"",
                "cherry"
            ];

            // when running cmd.exe we need /c to tell it to run the script; bash doesn't want any preamble for a script file
            string[] invocation = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ["/c", tempScript]
                : [tempScript];

            var executor = new ShellCommand(Command)
                .WithArguments([..invocation, ..inputArgs])
                .WithStdOutTarget(stdOut)
                .WithStdErrTarget(stdErr);

            var result = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);

            result.ExitCode.Should().Be(0, "the process should have run to completion");
            stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");

            var expectedQuotedValue = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "--thing=\\\"quotedValue\\\"" // on windows echo adds extra quoting; this is an artifact of cmd.exe not our code 
                : "--thing=\"quotedValue\"";

            stdOut.ToString()
                .Should()
                .Be(string.Join(Environment.NewLine,
                [
                    "apple",
                    "banana split", // spaces should be preserved
                    expectedQuotedValue,
                    "cherry",
                    "" // it has a trailing newline at the end
                ]));
        }
        finally
        {
            try
            {
                File.Delete(tempScript);
            }
            catch
            {
                // nothing to do if we can't delete the temp file
            }
        }
    }

    static string EchoEnvironmentVariable(string varName)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"%{varName}%" : $"${varName}";

    // Creates a script (.cmd or .sh) in the temp directory which echoes back its given command line arguments,
    // each on a newline, so we can use this to test how arguments are passed to the shell.
    // This function returns the path to the temporary script file.
    static string CreateScriptWhichEchoesBackArguments()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(tempFile,
                """
                @echo off
                setlocal enabledelayedexpansion
                for %%A in (%*) do (
                    echo %%~A
                )
                """);
            return tempFile;
        }
        else
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sh");
            File.WriteAllText(tempFile,
                """
                    for arg in "$@"; do
                        echo "$arg"
                    done
                    """.Replace("\r\n", "\n"));
            return tempFile;
        }
    }
}