using System.Text;

namespace Octopus.Shellfish;

// This is the NEW shellfish API. It is currently under development

/// <summary>
/// Holds the result of a shell command execution. Typically an exit code, but may also include stdout/stderr, etc
/// if those were configured to be captured.
/// </summary>
public class ShellCommandResult(int exitCode, StringBuilder? stdOutBuffer = null, StringBuilder? stdErrBuffer = null)
{

    /// <summary>
    /// The shell command exit code
    /// </summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>
    /// If CaptureStdOut() was configured, this will contain the stdout of the command.
    /// If not, it will be null
    /// </summary>
    public StringBuilder? StdOutBuffer { get; } = stdOutBuffer;

    /// <summary>
    /// If CaptureStdErr() was configured, this will contain the stdout of the command.
    /// If not, it will be null
    /// </summary>
    public StringBuilder? StdErrBuffer { get; } = stdErrBuffer;
}