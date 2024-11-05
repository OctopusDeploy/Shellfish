using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FluentAssertions;
using Octopus.Shellfish;
using Octopus.Shellfish.Plumbing;
using Octopus.Shellfish.Windows;
using Tests.Plumbing;
using Xunit;

namespace Tests
{
    public class ShellExecutorFixture
    {
        // ReSharper disable InconsistentNaming
        const int SIG_TERM = 143;
        const int SIG_KILL = 137;
        const string Username = "test-shellexecutor";

        static readonly string Command = PlatformDetection.IsRunningOnWindows ? "cmd.exe" : "bash";
        static readonly string CommandParam = PlatformDetection.IsRunningOnWindows ? "/c" : "-c";

        // Mimic the cancellation behaviour from LoggedTest in Octopus Server; we can't reference it in this assembly
        static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(45);

        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(TestTimeout); // overwritten in SetUp
        CancellationToken CancellationToken => cancellationTokenSource.Token;

        [Fact]
        public void ExitCode_ShouldBeReturned()
        {
            var arguments = $"{CommandParam} \"exit 99\"";
            var workingDirectory = "";
            var networkCredential = default(NetworkCredential);
            IDictionary<string, string>? customEnvironmentVariables = null;

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out var debugMessages,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            var expectedEncoding = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsEncodingHelper.GetOemEncoding() : Encoding.UTF8;

            exitCode.Should().Be(99, "our custom exit code should be reflected");

            debugMessages.ToString().Should().ContainEquivalentOf($"Starting {Command} in working directory '' using '{expectedEncoding.EncodingName}' encoding running as '{ProcessIdentity.CurrentUserName}'");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            infoMessages.ToString().Should().BeEmpty("no messages should be written to stdout");
        }

        [Fact]
        public void DebugLogging_ShouldContainDiagnosticsInfo_ForDefault()
        {
            var arguments = $"{CommandParam} \"echo hello\"";
            var workingDirectory = "";
            var networkCredential = default(NetworkCredential);
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out var debugMessages,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            debugMessages.ToString()
                .Should()
                .ContainEquivalentOf(Command, "the command should be logged")
                .And.ContainEquivalentOf(ProcessIdentity.CurrentUserName, "the current user details should be logged");
            infoMessages.ToString().Should().ContainEquivalentOf("hello");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        }

        [Fact]
        public void RunningAsSameUser_ShouldCopySpecialEnvironmentVariables()
        {
            var arguments = $"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"";
            var workingDirectory = "";
            var networkCredential = default(NetworkCredential);
            var customEnvironmentVariables = new Dictionary<string, string>
            {
                { "customenvironmentvariable", "customvalue" }
            };

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            infoMessages.ToString().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        }

        [PlatformSpecificFact(Platform.Windows)]
        public void DebugLogging_ShouldContainDiagnosticsInfo_DifferentUser()
        {
            var user = new TestUserPrincipal(Username);

            var arguments = $"{CommandParam} \"echo %userdomain%\\%username%\"";
            // Target the CommonApplicationData folder since this is a place the particular user can get to
            var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var networkCredential = user.GetCredential();
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out var debugMessages,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            debugMessages.ToString()
                .Should()
                .ContainEquivalentOf(Command, "the command should be logged")
                .And.ContainEquivalentOf($@"{user.DomainName}\{user.UserName}", "the custom user details should be logged")
                .And.ContainEquivalentOf(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "the working directory should be logged");
            infoMessages.ToString().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        }

        [PlatformSpecificFact(Platform.Windows)]
        public void RunningAsDifferentUser_ShouldCopySpecialEnvironmentVariables()
        {
            var user = new TestUserPrincipal(Username);

            var arguments = $"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"";
            // Target the CommonApplicationData folder since this is a place the particular user can get to
            var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var networkCredential = user.GetCredential();
            var customEnvironmentVariables = new Dictionary<string, string>
            {
                { "customenvironmentvariable", "customvalue" }
            };

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            infoMessages.ToString().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        }

        [PlatformSpecificFact(Platform.Windows)]
        public void RunningAsDifferentUser_ShouldWorkLotsOfTimes()
        {
            var user = new TestUserPrincipal(Username);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));

            for (var i = 0; i < 20; i++)
            {
                var arguments = $"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}%\"";
                // Target the CommonApplicationData folder since this is a place the particular user can get to
                var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var networkCredential = user.GetCredential();
                var customEnvironmentVariables = new Dictionary<string, string>
                {
                    { "customenvironmentvariable", $"customvalue-{i}" }
                };

                var exitCode = Execute(Command,
                    arguments,
                    workingDirectory,
                    out _,
                    out var infoMessages,
                    out var errorMessages,
                    networkCredential,
                    customEnvironmentVariables,
                    cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion");
                infoMessages.ToString().Should().ContainEquivalentOf($"customvalue-{i}", "the environment variable should have been copied to the child process");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            }
        }

        [PlatformSpecificFact(Platform.Windows)]
        public void RunningAsDifferentUser_CanWriteToItsOwnTempPath()
        {
            var user = new TestUserPrincipal(Username);

            var arguments = $"{CommandParam} \"echo hello > %temp%hello.txt\"";
            // Target the CommonApplicationData folder since this is a place the particular user can get to
            var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var networkCredential = user.GetCredential();
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out _,
                out _,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion after writing to the temp folder for the other user");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        }

        [Fact]
        // [Retry(3)] TODO retry with polly or something
        public void CancellationToken_ShouldForceKillTheProcess()
        {
            // Terminate the process after a very short time so the test doesn't run forever
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            // Starting a new instance of cmd.exe will run indefinitely waiting for user input
            var arguments = "";
            var workingDirectory = "";
            var networkCredential = default(NetworkCredential);
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                cts.Token);

            if (PlatformDetection.IsRunningOnWindows)
            {
                exitCode.Should().BeLessOrEqualTo(0, "the process should have been terminated");
                infoMessages.ToString().Should().ContainEquivalentOf("Microsoft Windows", "the default command-line header would be written to stdout");
            }
            else
            {
                exitCode.Should().BeOneOf(SIG_KILL, SIG_TERM, 0);
            }

            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        }

        [Fact]
        public void EchoHello_ShouldWriteToStdOut()
        {
            var arguments = $"{CommandParam} \"echo hello\"";
            var workingDirectory = "";
            var networkCredential = default(NetworkCredential);
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            infoMessages.ToString().Should().ContainEquivalentOf("hello");
        }

        [Fact]
        public void EchoError_ShouldWriteToStdErr()
        {
            var arguments = $"{CommandParam} \"echo Something went wrong! 1>&2\"";
            var workingDirectory = "";
            var networkCredential = default(NetworkCredential);
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            infoMessages.ToString().Should().BeEmpty("no messages should be written to stdout");
            errorMessages.ToString().Should().ContainEquivalentOf("Something went wrong!");
        }

        [Fact]
        public void RunAsCurrentUser_ShouldWork()
        {
            var arguments = PlatformDetection.IsRunningOnWindows
                ? $"{CommandParam} \"echo {EchoEnvironmentVariable("username")}\""
                : $"{CommandParam} \"whoami\"";
            var workingDirectory = "";
            var networkCredential = default(NetworkCredential);
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(Command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            infoMessages.ToString().Should().ContainEquivalentOf($@"{Environment.UserName}");
        }

        [PlatformSpecificTheory(Platform.Windows)]
        [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"")]
        public void RunAsCurrentUser_PowerShell_ShouldWork(string command, string arguments)
        {
            var workingDirectory = "";
            var networkCredential = default(NetworkCredential);
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            infoMessages.ToString().Should().ContainEquivalentOf($@"{Environment.UserDomainName}\{Environment.UserName}");
        }

        [PlatformSpecificTheory(Platform.Windows)]
        [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"")]
        [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"")]
        public void RunAsDifferentUser_ShouldWork(string command, string arguments)
        {
            var user = new TestUserPrincipal(Username);

            // Target the CommonApplicationData folder since this is a place the particular user can get to
            var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var networkCredential = user.GetCredential();
            var customEnvironmentVariables = new Dictionary<string, string>();

            var exitCode = Execute(command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                CancellationToken);

            exitCode.Should().Be(0, "the process should have run to completion");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            infoMessages.ToString().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
        }

        static string EchoEnvironmentVariable(string varName)
            => PlatformDetection.IsRunningOnWindows ? $"%{varName}%" : $"${varName}";

        static int Execute(
            string command,
            string arguments,
            string workingDirectory,
            out StringBuilder debugMessages,
            out StringBuilder infoMessages,
            out StringBuilder errorMessages,
            NetworkCredential? networkCredential,
            IDictionary<string, string>? customEnvironmentVariables,
            CancellationToken cancel
        )
        {
            var debug = new StringBuilder();
            var info = new StringBuilder();
            var error = new StringBuilder();
            var exitCode = ShellExecutor.ExecuteCommand(
                command,
                arguments,
                workingDirectory,
                x =>
                {
                    Console.WriteLine($"{DateTime.UtcNow} DBG: {x}");
                    debug.Append(x);
                },
                x =>
                {
                    Console.WriteLine($"{DateTime.UtcNow} INF: {x}");
                    info.Append(x);
                },
                x =>
                {
                    Console.WriteLine($"{DateTime.UtcNow} ERR: {x}");
                    error.Append(x);
                },
                networkCredential,
                customEnvironmentVariables,
                cancel);

            debugMessages = debug;
            infoMessages = info;
            errorMessages = error;

            return exitCode;
        }
    }
}