using System;
using System.IO;

namespace FactorySeatCompanion.SimHub;

// Picks the companion configuration when the plugin's ConfigPath setting is empty, so an
// installed copy works without editing SimHub's settings file. SimHub-free for the tests.
public static class ConfigLocator
{
    public const string FileName = "companion.json";

    // An explicit ConfigPath wins. Otherwise a player's own %AppData%\FactorySeatCompanion\companion.json,
    // then the companion.json the installer ships next to the CLI. Null means the CLI's built-in defaults.
    public static string? Resolve(string configured, string cliPath, string appData, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        string userConfig = Path.Combine(appData, "FactorySeatCompanion", FileName);
        if (exists(userConfig)) return userConfig;
        string shipped = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cliPath)) ?? "", FileName);
        return exists(shipped) ? shipped : null;
    }
}
