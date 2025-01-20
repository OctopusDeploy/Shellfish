﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
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

// Cross-platform tests for ShellCommand
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

        IReadOnlyDictionary<string, string> envVars = new Dictionary<string, string>
        {
            { "customenvironmentvariable", "customvalue" }
        };

        var executor = new ShellCommand(Command)
            .WithArguments($"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"")
            .WithEnvironmentVariables(envVars)
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
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        // Terminate the process after a short time so the test doesn't run forever
        cts.CancelAfter(TimeSpan.FromSeconds(0.5));

        var executor = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ShellCommand("timeout.exe").WithArguments("/t 500 /nobreak")
            : new ShellCommand("bash").WithArguments("-c \"sleep 500\"");

        Process? process = null;
        executor = executor
            .CaptureProcess(p => process = p);
            // Do not capture stdout or stderr; the windows timeout command will fail with ERROR: Input redirection is not supported

        var cancellationToken = cts.Token;
        if (behaviour == SyncBehaviour.Async)
        {
            await executor.Invoking(e => e.ExecuteAsync(cancellationToken)).Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            executor.Invoking(e => e.Execute(cancellationToken)).Should().Throw<OperationCanceledException>();
        }
        
        // we can't observe any exit code because Execute() threw an exception

        process?.Should().NotBeNull();
        EnsureProcessHasExited(process!);
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task CancellationToken_ShouldForceKillTheProcess_DoNotThrowOnCancellation(SyncBehaviour behaviour)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        // Terminate the process after a short time so the test doesn't run forever
        cts.CancelAfter(TimeSpan.FromSeconds(0.5));
        
        var executor = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ShellCommand("timeout.exe").WithArguments("/t 500 /nobreak")
            : new ShellCommand("bash").WithArguments("-c \"sleep 500\"");

        Process? process = null;
        executor = executor
            .WithOptions(ShellCommandOptions.DoNotThrowOnCancellation)
            .CaptureProcess(p => process = p);
            // Do not capture stdout or stderr; the windows timeout command will fail with ERROR: Input redirection is not supported

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(cts.Token)
            : executor.Execute(cts.Token);

        var exitCode = result.ExitCode;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            exitCode.Should().BeLessOrEqualTo(0, "the process should have been terminated");
        }
        else
        {
            exitCode.Should().BeOneOf(SIG_KILL, SIG_TERM, 0, -1);
        }

        process?.Should().NotBeNull();
        EnsureProcessHasExited(process!);
    }

    static void EnsureProcessHasExited(Process process)
    {
        try
        {
            process.HasExited.Should().BeTrue("the process should have exited");
        }
        catch (InvalidOperationException e) when (e.Message is "No process is associated with this object.")
        {
            // process.HasExited throws this exception if you call HasExited on a process that has quit already; we expect this
        }
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
        using var tempScript = TempScript.Create(
            cmd: """
                 @echo off
                 setlocal enabledelayedexpansion
                 for %%A in (%*) do (
                     echo %%~A
                 )
                 """,
            sh: """
                for arg in "$@"; do
                    echo "$arg"
                done
                """);

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
        string[] runScriptArgs = tempScript.GetCommandArgs();

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments([..runScriptArgs, ..inputArgs])
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

    static string EchoEnvironmentVariable(string varName)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"%{varName}%" : $"${varName}";
}