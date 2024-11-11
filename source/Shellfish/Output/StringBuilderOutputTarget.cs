using System;
using System.Text;
using System.Threading;

namespace Octopus.Shellfish.Output;

public class StringBuilderOutputTarget(StringBuilder stringBuilder) : IOutputTarget
{
    int hasWritten;

    public void WriteLine(string? line)
    {
        if (!string.IsNullOrEmpty(line))
        {
            if (Interlocked.Exchange(ref hasWritten, 1) == 1)
            {
                stringBuilder.AppendLine();
            }
            stringBuilder.Append(line);
        }
    }
}