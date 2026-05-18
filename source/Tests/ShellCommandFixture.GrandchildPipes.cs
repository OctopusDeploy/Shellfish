using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

namespace Tests;

// Reproduces the "grandchild inherits redirected pipes and outlives the immediate child" hang
// on the SYNCHRONOUS Execute path.
//
// Scenario: bash spawns a background `sleep` (with `&`). The sleep inherits bash's stdout pipe
// (which is the redirected pipe Shellfish set up). bash then exits with 0. The Process.Exited
// event fires, ShellfishProcess.WaitForExit unblocks past `exitedEvent.Wait`, and then calls
// the parameterless `process.WaitForExit()` to drain the async stream readers. That call
// blocks forever — the orphaned `sleep` still holds the write-end of the pipe so the readers
// never see EOF. The cancellation token does not help here because the parameterless overload
// does not accept one.
//
// The async path (ExecuteAsync) passes the token through to WaitForExitAsync which IS
// cancellable, so the bug only manifests in synchronous Execute.
//
// The test guards against an actual infinite hang by running Execute on a Task and timing
// out, so we report a test failure rather than blocking the runner forever.
public class ShellCommandFixture_GrandchildPipes
{
    static readonly TimeSpan HangGuardTimeout = TimeSpan.FromSeconds(15);

    [NixFact]
    public void Execute_WhenGrandchildHoldsRedirectedPipes_ShouldNotHang()
    {
        var grandchildPidFile = Path.Combine(Path.GetTempPath(), $"shellfish-grandchild-{Guid.NewGuid():N}.pid");

        // sleep is backgrounded with `&` so it inherits bash's redirected stdout but bash exits
        // immediately. The grandchild PID is captured so we can clean it up afterwards.
        var script = $"sleep 30 & echo $! > {grandchildPidFile}; exit 0";

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand("bash")
            .WithArguments(new[] { "-c", script })
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        try
        {
            var executeTask = Task.Run(() => executor.Execute(CancellationToken.None));

            executeTask.Wait(HangGuardTimeout)
                .Should()
                .BeTrue($"Execute() should return within {HangGuardTimeout.TotalSeconds}s even when a grandchild inherits the redirected pipes; if this assertion fails, the parameterless Process.WaitForExit() in ShellfishProcess.WaitForExit is hanging on the async-reader drain.");
        }
        finally
        {
            TryKillGrandchild(grandchildPidFile);
        }
    }

    static void TryKillGrandchild(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile)) return;
            var pidText = File.ReadAllText(pidFile).Trim();
            if (!int.TryParse(pidText, out var pid)) return;

            try
            {
                var p = Process.GetProcessById(pid);
                p.Kill();
            }
            catch
            {
                // already gone
            }
        }
        catch
        {
            // best-effort cleanup
        }
        finally
        {
            try { if (File.Exists(pidFile)) File.Delete(pidFile); } catch { /* ignore */ }
        }
    }
}
