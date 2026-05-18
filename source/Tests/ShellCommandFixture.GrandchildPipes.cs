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

// Mirrors the Tentacle SilentProcessRunner tests for the
// "grandchild inherits redirected pipes and outlives the immediate child" hang.
//
// Scenario in both tests:
//   1. Shellfish launches a child with redirected stdout/stderr.
//   2. The child spawns a grandchild that inherits the redirected pipe write-ends.
//   3. The child exits, leaving the grandchild orphaned and still holding the pipes.
//   4. We cancel the CancellationToken.
//   5. We expect Execute() to return promptly — kill the tree, release the pipes,
//      and propagate OperationCanceledException.
//
// If the underlying Process.WaitForExit() drains the async readers without honouring
// the token, the call hangs forever because the readers never see EOF. The test
// guards against an actual infinite hang by running Execute on a Task with a deadline,
// so we report a failure rather than blocking the runner.
public class ShellCommandFixture_GrandchildPipes
{
    static readonly TimeSpan HangGuardTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan GrandchildSpawnTimeout = TimeSpan.FromSeconds(30);

    [NixFact]
    public void Execute_WhenUnixGrandchildHoldsRedirectedPipes_ShouldNotHangAfterCancellation()
    {
        var grandchildPidFile = Path.Combine(Path.GetTempPath(), $"shellfish-grandchild-{Guid.NewGuid():N}.pid");

        // sh -c "sleep 600 & echo $! > pidfile; exit 0"
        // sh backgrounds sleep (which inherits sh's redirected stdout), writes the PID, exits.
        // sleep is reparented to init/launchd and keeps the pipe open.
        var script = $"sleep 600 & echo $! > '{grandchildPidFile}'; exit 0";

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand("/bin/sh")
            .WithArguments(new[] { "-c", script })
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        try
        {
            using var cts = new CancellationTokenSource();
            var executeTask = Task.Run(() =>
            {
                try { executor.Execute(cts.Token); }
                catch (OperationCanceledException) { /* expected */ }
            });

            WaitForGrandchildSpawn(grandchildPidFile, GrandchildSpawnTimeout);

            var sw = Stopwatch.StartNew();
            cts.Cancel();

            var completed = executeTask.Wait(HangGuardTimeout);
            sw.Stop();

            completed.Should().BeTrue(
                $"Execute() should return shortly after cancellation even when a Unix grandchild (reparented to init/launchd) " +
                $"holds the redirected pipes. Elapsed since cancel: {sw.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            TryKillGrandchild(grandchildPidFile);
        }
    }

    [WindowsFact]
    public void Execute_WhenWindowsGrandchildHoldsRedirectedPipes_ShouldNotHangAfterCancellation()
    {
        // Chain: PowerShell (immediate child) -> cmd.exe -> ping.exe (grandchild).
        //   1. PowerShell spawns cmd via System.Diagnostics.Process.
        //      Setting RedirectStandardInput is what flips bInheritHandles=true in .NET's
        //      Process.Start, so cmd inherits PowerShell's stdout/stderr — themselves our
        //      redirected pipes.
        //   2. cmd runs `start /b ping ...`, spawning ping with inherited handles, then
        //      exits. cmd exiting before we cancel breaks the PPID chain so Kill(true) can't
        //      find ping by tree-walk.
        //   3. PowerShell finds ping's PID via WMI (for cleanup) and waits to be killed by
        //      cancellation.
        var grandchildPidFile = Path.Combine(Path.GetTempPath(), $"shellfish-grandchild-{Guid.NewGuid():N}.pid");

        var psScript = @"
$pidFile = 'PIDFILE_PLACEHOLDER'
$pingPath = Join-Path $env:WINDIR 'System32\PING.EXE'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = Join-Path $env:WINDIR 'System32\cmd.exe'
$psi.Arguments = '/c start /b """" ""' + $pingPath + '"" -n 60000 127.0.0.1'
$psi.UseShellExecute = $false
$psi.CreateNoWindow  = $true
# Redirecting any stream flips bInheritHandles=true in .NET's Process.Start.
$psi.RedirectStandardInput = $true
$cmd = [System.Diagnostics.Process]::Start($psi)
$cmd.StandardInput.Close()
$cmdPid = $cmd.Id
# Wait for cmd to exit — breaks the PPID chain so Kill(true) misses ping.
$cmd.WaitForExit()
# Poll until ping appears in WMI; there's a lag between cmd exiting and the
# orphaned ping becoming visible.
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline) {
    $p = Get-CimInstance Win32_Process -Filter ""ParentProcessId=$cmdPid AND Name='PING.EXE'"" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($p) { Set-Content -Path $pidFile -Value $p.ProcessId; break }
    Start-Sleep -Milliseconds 100
}
# Park PowerShell so it's still alive when we cancel — we want the cancel path
# (kill + return) to exercise the hang, not a clean exit.
Start-Sleep -Seconds 600
";
        psScript = psScript.Replace("PIDFILE_PLACEHOLDER", grandchildPidFile);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand("powershell.exe")
            .WithArguments(new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded })
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        try
        {
            using var cts = new CancellationTokenSource();
            var executeTask = Task.Run(() =>
            {
                try { executor.Execute(cts.Token); }
                catch (OperationCanceledException) { /* expected */ }
            });

            WaitForGrandchildSpawn(grandchildPidFile, GrandchildSpawnTimeout);

            var sw = Stopwatch.StartNew();
            cts.Cancel();

            var completed = executeTask.Wait(HangGuardTimeout);
            sw.Stop();

            completed.Should().BeTrue(
                $"Execute() should return shortly after cancellation even when a Windows grandchild " +
                $"holds the redirected pipes. Elapsed since cancel: {sw.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            TryKillGrandchild(grandchildPidFile);
        }
    }

    static void WaitForGrandchildSpawn(string pidFile, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(pidFile) && int.TryParse(SafelyReadAllText(pidFile).Trim(), out var pid) && pid > 0)
                return;
            Thread.Sleep(50);
        }
        throw new TimeoutException(
            $"Test setup failed: the grandchild PID was never written to '{pidFile}'. " +
            $"The grandchild-pipe scenario is not being exercised.");
    }

    static string SafelyReadAllText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return string.Empty; }
    }

    static void TryKillGrandchild(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile)) return;
            var pidText = SafelyReadAllText(pidFile).Trim();
            if (!int.TryParse(pidText, out var pid)) return;

            try { Process.GetProcessById(pid).Kill(); }
            catch { /* already gone */ }
        }
        catch { /* best-effort cleanup */ }
        finally
        {
            try { if (File.Exists(pidFile)) File.Delete(pidFile); } catch { /* ignore */ }
        }
    }
}
