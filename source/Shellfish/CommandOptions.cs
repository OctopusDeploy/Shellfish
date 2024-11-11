namespace Octopus.Shellfish;

public class CommandOptions
{
    public bool KillProcessOnCancellation { get; set; } = true;
    public bool ThrowExceptionOnCancellation { get; set; } = false;
}