using System;
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
        // Terminate the process after a very short time so the test doesn't run forever
        cts.CancelAfter(TimeSpan.FromSeconds(1));

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
    public async Task StreamingStdInputShouldWork(SyncBehaviour behaviour)
    {
        using var tempScript = CreateTempScript(
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

        // when running cmd.exe we need /c to tell it to run the script; bash doesn't want any preamble for a script file
        string[] args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["/c", tempScript.ScriptPath]
            : [tempScript.ScriptPath];

        // it's going to ask us for the name first, so we need to answer back or the process will stall forever
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            Octobob
            """));

        var executor = new ShellCommand(Command)
            .WithArguments(args)
            .WithStdIn(stream)
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter Name:" + Environment.NewLine + "Hello Octobob" + Environment.NewLine);
    }

    [Theory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task StreamingStdInputShouldBeCancellable(SyncBehaviour behaviour)
    {
        using var tempScript = CreateTempScript(
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

        // when running cmd.exe we need /c to tell it to run the script; bash doesn't want any preamble for a script file
        string[] args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["/c", tempScript.ScriptPath]
            : [tempScript.ScriptPath];

        // it's going to ask us for the name first, but we don't give it anything; the script should hang
        var stream = new SlowStream(TimeSpan.FromSeconds(10));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        var executor = new ShellCommand(Command)
            .WithArguments(args)
            .WithStdIn(stream)
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

        result.ExitCode.Should().Be(-1, "the process should have been killed");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter Name:" + Environment.NewLine);
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
        using var tempScript = CreateTempScript(
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
        string[] invocation = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["/c", tempScript.ScriptPath]
            : [tempScript.ScriptPath];

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

    static string EchoEnvironmentVariable(string varName)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"%{varName}%" : $"${varName}";

    // Some interactions such as stdout or encoding codepages require things that don't work with an inline cmd /c or bash -c command
    // This helper writes a script file into the temp directory so we can exercise more complex scenarios
    static TempScriptHandle CreateTempScript(string cmd, string sh)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(tempFile, cmd);
            return new TempScriptHandle(tempFile);
        }
        else
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sh");
            File.WriteAllText(tempFile, sh.Replace("\r\n", "\n"));
            return new TempScriptHandle(tempFile);
        }
    }

    class TempScriptHandle(string scriptPath) : IDisposable
    {
        public string ScriptPath { get; } = scriptPath;

        public void Dispose()
        {
            try
            {
                File.Delete(ScriptPath);
            }
            catch
            {
                // nothing to do if we can't delete the temp file
            }
        }
    }

    class SlowStream(TimeSpan sleepOnReadDuration) : Stream
    {
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

        public override void SetLength(long value) => throw new NotImplementedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(sleepOnReadDuration);
            return 0;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(sleepOnReadDuration, cancellationToken);
            return 0;
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get; set; }
    }
}