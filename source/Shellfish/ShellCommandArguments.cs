namespace Octopus.Shellfish;

abstract class ShellCommandArguments
{
    public static NoArgumentsType None { get; } = new();
    public static StringType String(string value) => new(value);
    public static ArgumentListType List(string[] value) => new(value);
    
    // Don't construct this type directly, use ShellCommandArguments.None
    public class NoArgumentsType : ShellCommandArguments;
    
    // Don't construct this type directly, use ShellCommandArguments.String(value)
    public class StringType(string value) : ShellCommandArguments
    {
        public string Value { get; } = value;
    }
    
    // Don't construct this type directly, use ShellCommandArguments.List(value)
    public class ArgumentListType(string[] values) : ShellCommandArguments
    {
        public string[] Values { get; } = values;
    }
}