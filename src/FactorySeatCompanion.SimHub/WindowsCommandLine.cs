using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FactorySeatCompanion.SimHub;

// net48 has no ProcessStartInfo.ArgumentList; quote per CommandLineToArgvW rules.
public static class WindowsCommandLine
{
    public static string Join(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
        var result = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            // Backslashes are literal unless they precede a quote.
            result.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            result.Append(c);
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
