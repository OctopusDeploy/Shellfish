using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

// We disable this because we want to test the synchronous version of the method as well as the async version using the same test method
// ReSharper disable MethodHasAsyncOverload

namespace Tests;

// Windows-specific tests for ShellCommandExecutor
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("Windows")]
#endif
public class ShellCommandExecutorFixtureWindows(ShellCommandExecutorFixtureWindows.WindowsUserClassFixture fx) : IClassFixture<ShellCommandExecutorFixtureWindows.WindowsUserClassFixture>
{
    // Note: This leaves the user account lying around on your PC. We should probably delete it but it's the same account each time so not a big deal.
    public class WindowsUserClassFixture
    {
        internal TestUserPrincipal User { get; } = new(Username);
    }

    readonly TestUserPrincipal user = fx.User;

    const string Username = "test-shellexecutor";

    readonly CancellationTokenSource cancellationTokenSource = new(ShellCommandExecutorFixture.TestTimeout);
    CancellationToken CancellationToken => cancellationTokenSource.Token;

    [WindowsTheory]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Sync)]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Async)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Sync)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Async)]
    public async Task RunAsCurrentUser_CmdAndPowerShell_ShouldWork(string command, string arguments, SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable(command)
            .WithRawArguments(arguments)
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().Be(Environment.NewLine, "no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf($@"{Environment.UserDomainName}\{Environment.UserName}");
    }

    [WindowsTheory]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Sync)]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Async)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Sync)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Async)]
    public async Task RunAsDifferentUser_ShouldWork(string command, string arguments, SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable(command)
            .WithRawArguments(arguments)
            .RunAsUser(user.GetCredential())
            // Target the CommonApplicationData folder since this is a place the particular user can get to
            .WithWorkingDirectory(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToStringWithoutTrailingWhitespace().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToStringWithoutTrailingWhitespace().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
    }

    [WindowsTheory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task RunningAsDifferentUser_ShouldCopySpecialEnvironmentVariables(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommandExecutor()
            .WithExecutable("cmd.exe")
            .WithRawArguments($"/c \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"")
            .RunAsUser(user.GetCredential())
            .WithEnvironmentVariables(new Dictionary<string, string>
            {
                { "customenvironmentvariable", "customvalue" }
            })
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToStringWithoutTrailingWhitespace().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToStringWithoutTrailingWhitespace().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
    }

    [WindowsTheory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task RunningAsDifferentUser_ShouldWorkLotsOfTimes(SyncBehaviour behaviour)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));

        var executor = new ShellCommandExecutor()
            .WithExecutable("cmd.exe")
            .WithRawArguments($"/c \"echo {EchoEnvironmentVariable("customenvironmentvariable")}%\"")
            .RunAsUser(user.GetCredential())
            .WithWorkingDirectory(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        for (var i = 0; i < 20; i++)
        {
            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

            // the executor is mutable so this overwrites the config each time
            executor
                .CaptureStdOutTo(stdOut)
                .CaptureStdErrTo(stdErr)
                .WithEnvironmentVariables(new Dictionary<string, string>
                {
                    { "customenvironmentvariable", $"customvalue-{i}" }
                });

            var result = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);

            result.ExitCode.Should().Be(0, "the process should have run to completion");
            stdOut.ToStringWithoutTrailingWhitespace().Should().ContainEquivalentOf($"customvalue-{i}", "the environment variable should have been copied to the child process");
            stdErr.ToStringWithoutTrailingWhitespace().Should().BeEmpty("no messages should be written to stderr");
        }
    }

    [WindowsTheory, InlineData(SyncBehaviour.Sync), InlineData(SyncBehaviour.Async)]
    public async Task RunningAsDifferentUser_CanWriteToItsOwnTempPath(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var uniqueString = Guid.NewGuid().ToString("N");

        var executor = new ShellCommandExecutor()
            .WithExecutable("cmd.exe")
            // Prove we can write to the temp folder by reading the contents back and echoing them into our test 
            .WithRawArguments($"/c \"echo {uniqueString} > %temp%\\{uniqueString}.txt && type %temp%\\{uniqueString}.txt\"")
            .RunAsUser(user.GetCredential())
            .CaptureStdOutTo(stdOut)
            .CaptureStdErrTo(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion after writing to the temp folder for the other user");
        stdErr.ToStringWithoutTrailingWhitespace().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToStringWithoutTrailingWhitespace().Should().Contain(uniqueString);
    }

    static string EchoEnvironmentVariable(string varName) => $"%{varName}%";
}