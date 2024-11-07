using System;
using System.Text;

namespace Octopus.Shellfish;

public static class ShellCommandExtensionMethods
{
    /// <summary>
    /// If the executable has the .dll extension, then this method will prepend the executable with `dotnet` to run it.
    /// We assume `dotnet` is in the path.
    /// </summary>
    /// <remarks>
    /// This is not really a part of the core library; we have it because it is useful for Octopus,
    /// and to serve as a lighthouse example of how to use BeforeStartHook to extend the behavior of ShellCommand.
    /// </remarks>
    public static ShellCommand ExecutableCanBeDotnetDll(this ShellCommand shellCommand)
    {
        return shellCommand.BeforeStartHook(process =>
        {
            // If the executable is a dll, then we need to run it with dotnet <dll> <args>
            var executable = process.StartInfo.FileName;
            if (!executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return; // nothing to do here

            // Our executable becomes "dotnet" to host the dll
            process.StartInfo.FileName = "dotnet"; // assume dotnet is in the path
            
#if NET5_0_OR_GREATER
            // if we are using ArgumentList, then we can just prepend and we're done
            if(process.StartInfo.ArgumentList is { Count: > 0 })
            {
                process.StartInfo.ArgumentList.Insert(0, executable);
                return;
            }
#endif                
            // if we have a string argumentlist, we need to prepend to that.
            if (!string.IsNullOrEmpty(process.StartInfo.Arguments))
            {
                var sb = new StringBuilder();
                PasteArguments.AppendArgument(sb, executable); // quote the executable if needed
                sb.Append(" ");
                sb.Append(process.StartInfo.Arguments);
                process.StartInfo.Arguments = sb.ToString();
                return;
            }
            
            // else we have no prior arguments, the executable becomes the only argument to `dotnet`
            process.StartInfo.Arguments = executable;
        });
    }
}