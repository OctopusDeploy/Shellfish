using System.Text;

namespace Tests.Plumbing;

public static class StringBuilderExtensionMethods
{
    /// <summary>
    /// Shell commands will often print trailing newlines after their output.
    /// The behaviour is not entirely consistent between different commands and platforms, and
    /// isn't relevant to our tests, so we strip them off to make assertions simpler.
    /// </summary>
    public static string ToStringWithoutTrailingWhitespace(this StringBuilder stringBuilder)
    {
        int length = stringBuilder.Length;
        while (length > 0 && char.IsWhiteSpace(stringBuilder[length - 1]))
            length--;

        return stringBuilder.ToString(0, length);
    }
}