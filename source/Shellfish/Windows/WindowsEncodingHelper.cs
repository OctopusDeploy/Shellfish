using System;
using System.Text;

namespace Octopus.Shellfish.Windows;

static class WindowsEncodingHelper
{
    public static Encoding GetOemEncoding()
    {
        try
        {
            // Get the OEM CodePage for the installation, otherwise fall back to code page 850 (DOS Western Europe)
            // https://en.wikipedia.org/wiki/Code_page_850
            const int CP_OEMCP = 1;
            const int dwFlags = 0;
            const int CodePage850 = 850;

            var codepage = Interop.Kernel32.GetCPInfoEx(CP_OEMCP, dwFlags, out var info)
                ? info.CodePage
                : CodePage850;

            var encoding = Encoding.GetEncoding(codepage);
            return encoding;
        }
        catch
        {
            // Fall back to UTF8 if everything goes wrong
            return Encoding.UTF8;
        }
    }
}