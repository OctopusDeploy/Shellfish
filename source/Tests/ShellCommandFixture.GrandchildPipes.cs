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
    public void Execute_WhenUnixGrandchildHoldsRedirectedPipes_ShouldNotHang()
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

    [WindowsFact]
    public void Execute_WhenWindowsGrandchildHoldsRedirectedPipes_ShouldNotHang()
    {
        // Windows equivalent of the [NixFact] above. We need a grandchild that inherits the
        // redirected stdout/stderr write-ends and outlives the immediate child.
        //
        // Chain: PowerShell (immediate child) -> cmd.exe -> ping.exe (grandchild).
        //   1. PowerShell spawns cmd via System.Diagnostics.Process. Setting
        //      RedirectStandardInput on the ProcessStartInfo is what flips
        //      bInheritHandles=true in .NET's Process.Start — so cmd inherits PowerShell's
        //      stdout/stderr, which are themselves our redirected pipes.
        //   2. cmd runs `start /b ping ...`, spawning ping with inherited handles, then exits.
        //   3. PowerShell finds ping's PID via WMI (for cleanup) and exits cleanly.
        //
        // From Shellfish's POV: the immediate child (PowerShell) exited normally so
        // `exitedEvent` is released and ShellfishProcess.WaitForExit falls through to
        // `process.WaitForExit()` (the non-cancellable parameterless overload) to drain the
        // async readers. ping still holds the pipe write-ends, so the readers never EOF and
        // the call hangs forever.
        var grandchildPidFile = Path.Combine(Path.GetTempPath(), $"shellfish-grandchild-{Guid.NewGuid():N}.pid");

        var psScript = @"
$pidFile = 'PIDFILE_PLACEHOLDER'
$pingPath = Join-Path $env:WINDIR 'System32\PING.EXE'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = Join-Path $env:WINDIR 'System32\cmd.exe'
# -n 60000 just makes ping long-running enough to outlive the test.
$psi.Arguments = '/c start /b """" ""' + $pingPath + '"" -n 60000 127.0.0.1'
$psi.UseShellExecute = $false
$psi.CreateNoWindow  = $true
# Redirecting any stream flips bInheritHandles=true in .NET's Process.Start,
# so non-redirected streams pass through via GetStdHandle to the child.
$psi.RedirectStandardInput = $true
$cmd = [System.Diagnostics.Process]::Start($psi)
$cmd.StandardInput.Close()
$cmdPid = $cmd.Id
# Wait for cmd to exit — by the time PowerShell itself exits we want ping to be orphaned.
$cmd.WaitForExit()
# Poll until ping appears in WMI — there's a lag between cmd exiting and the
# orphaned ping becoming visible.
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline) {
    $p = Get-CimInstance Win32_Process -Filter ""ParentProcessId=$cmdPid AND Name='PING.EXE'"" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($p) { Set-Content -Path $pidFile -Value $p.ProcessId; break }
    Start-Sleep -Milliseconds 100
}
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
