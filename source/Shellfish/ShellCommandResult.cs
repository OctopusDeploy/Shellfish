using System.Text;

namespace Octopus.Shellfish;

// This is the NEW shellfish API. It is currently under development

/// <summary>
/// Holds the result of a shell command execution. Typically an exit code, but may also include stdout/stderr, etc
/// if those were configured to be captured.
/// </summary>
public class ShellCommandResult(int exitCode)
{
    /// <summary>
    /// The shell command exit code
    /// </summary>
    public int ExitCode { get; } = exitCode;
}