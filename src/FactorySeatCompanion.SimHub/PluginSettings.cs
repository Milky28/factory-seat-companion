namespace FactorySeatCompanion.SimHub;

// Stored by SimHub as common plugin settings. A settings UI is Phase 3.
public sealed class PluginSettings
{
    // Empty means FactorySeatCompanion\FactorySeatCompanion.Cli.exe next to the plugin DLL.
    public string CliPath { get; set; } = "";
    // Empty means the CLI's default: Factory Seat's %AppData%\LmuCareer\briefing.json.
    public string BriefingPath { get; set; } = "";
    // Empty means %AppData%\FactorySeatCompanion\companion.json if present, else the installed
    // FactorySeatCompanion\companion.json next to the CLI (see ConfigLocator).
    public string ConfigPath { get; set; } = "";
    // Synthetic fixture directory for dashboard testing only; never live verification.
    public string FixturesPath { get; set; } = "";
    public double ReportMaxAgeSeconds { get; set; } = 10;
}
