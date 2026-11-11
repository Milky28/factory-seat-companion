using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using GameReaderCommon;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimHub.Plugins;

namespace FactorySeatCompanion.SimHub;

// Properties appear in SimHub as FactorySeatPlugin.<Name>, for example FactorySeatPlugin.ReadyToRace.
[PluginDescription("Read-only check of LMU race setup against the Factory Seat round briefing.")]
[PluginAuthor("Factory Seat Companion")]
[PluginName("Factory Seat Companion")]
public sealed class FactorySeatPlugin : IPlugin, IDataPlugin
{
    private const string SettingsName = "GeneralSettings";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

    private PluginSettings settings = new PluginSettings();
    private CompanionBridge? bridge;
    private readonly OverlayVisibility visibility = new OverlayVisibility();
    private readonly LmuProcess lmu = new LmuProcess();
    private Tuple<DateTimeOffset, DashboardState>? cached;

    public PluginManager PluginManager { get; set; } = null!;

    public void Init(PluginManager pluginManager)
    {
        settings = this.ReadCommonSettings(SettingsName, () => new PluginSettings());
        var pluginDirectory = Path.GetDirectoryName(typeof(FactorySeatPlugin).Assembly.Location) ?? "";
        bridge = new CompanionBridge(settings,
            Path.Combine(pluginDirectory, "FactorySeatCompanion", "FactorySeatCompanion.Cli.exe"));

        this.AttachDelegate("ReadyToRace", () => State().ReadyToRace);
        this.AttachDelegate("OverallStatus", () => State().OverallStatus);
        this.AttachDelegate("Headline", () => State().Headline);
        this.AttachDelegate("Mode", () => State().Mode);
        this.AttachDelegate("BriefingStatus", () => State().BriefingStatus);
        this.AttachDelegate("LmuStatus", () => State().LmuStatus);
        this.AttachDelegate("ReportAgeSeconds", () => State().ReportAgeSeconds);
        this.AttachDelegate("BridgeStatus", () => State().BridgeStatus);
        this.AttachDelegate("Diagnostics", () => State().Diagnostics);
        this.AttachDelegate("Hints", () => State().Hints);
        this.AttachDelegate("ManualCheck", () => State().ManualCheck);
        this.AttachDelegate("Stakes", () => State().Stakes);
        this.AttachDelegate("Weekend", () => State().Weekend);
        this.AttachDelegate("TestDay", () => State().TestDay);
        this.AttachDelegate("Screen", () => { State(); return visibility.Screen; });
        this.AttachDelegate("ShowOverlay", () => { State(); return visibility.Visible; });
        // Bind to a key or wheel button in SimHub's Controls and events to hide or show the overlay.
        this.AddAction("ToggleOverlay", (a, b) => visibility.Toggle());
        // Bind to a key or wheel button to press Start race weekend in Factory Seat (1.5) without leaving LMU.
        this.AddAction("StartRaceWeekend", (a, b) => RequestStartWeekend());
        for (int i = 0; i < DashboardState.RowSlots; i++)
        {
            int slot = i;
            string prefix = "Row" + (slot + 1).ToString("00", CultureInfo.InvariantCulture) + ".";
            this.AttachDelegate(prefix + "Visible", () => State().Rows[slot].Visible);
            this.AttachDelegate(prefix + "Id", () => State().Rows[slot].Id);
            this.AttachDelegate(prefix + "Label", () => State().Rows[slot].Label);
            this.AttachDelegate(prefix + "Required", () => State().Rows[slot].Required);
            this.AttachDelegate(prefix + "Current", () => State().Rows[slot].Current);
            this.AttachDelegate(prefix + "Status", () => State().Rows[slot].Status);
            this.AttachDelegate(prefix + "StatusText", () => State().Rows[slot].StatusText);
            this.AttachDelegate(prefix + "Icon", () => State().Rows[slot].Icon);
            this.AttachDelegate(prefix + "Hint", () => State().Rows[slot].Hint);
            this.AttachDelegate(prefix + "Reason", () => State().Rows[slot].Reason);
        }
        bridge.Start();
    }

    // Telemetry may not flow in LMU menus, so nothing here depends on DataUpdate being called.
    public void DataUpdate(PluginManager pluginManager, ref GameData data) { }

    public void End(PluginManager pluginManager)
    {
        bridge?.Dispose();
        this.SaveCommonSettings(SettingsName, settings);
    }

    // Reads briefing.json afresh at the press, so the request always carries the career open right now,
    // and writes start-weekend.request only when Factory Seat could act on it. The banner flips to
    // Started once Factory Seat rewrites the briefing with the round Armed.
    private void RequestStartWeekend()
    {
        try
        {
            string path = string.IsNullOrWhiteSpace(settings.BriefingPath) ? DefaultBriefingPath() : settings.BriefingPath;
            var briefing = JObject.Parse(File.ReadAllText(path));
            if (Text(briefing["format"]) != "factory-seat-briefing") return;
            bool? open = briefing["open"]?.Type == JTokenType.Boolean ? (bool)briefing["open"]! : (bool?)null;
            string? careerId = Text(briefing["career"]?["id"]);
            if (StartWeekendRequest.Refusal(FactorySeatRunning(), open, Text(briefing["round"]?["state"]), careerId) == null)
                StartWeekendRequest.Write(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", careerId!);
        }
        // Nothing to start, or the briefing is mid-write: the press does nothing, like Factory Seat's own checks.
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException ||
            ex is ArgumentException || ex is InvalidOperationException) { }
    }

    private static string? Text(JToken? token) => token?.Type == JTokenType.String ? (string?)token : null;

    // Factory Seat writes next to its saves: %AppData%\LmuCareer, or LMUCAREER_HOME when set.
    private static string DefaultBriefingPath()
    {
        var home = Environment.GetEnvironmentVariable("LMUCAREER_HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LmuCareer");
        return Path.Combine(home, "briefing.json");
    }

    private static bool FactorySeatRunning()
    {
        var processes = Process.GetProcessesByName("FactorySeat");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    // Delegates are read many times per frame; rebuild at most every 250 ms. Age limits are
    // enforced on every rebuild, even when the companion stops sending reports.
    private DashboardState State()
    {
        var now = DateTimeOffset.UtcNow;
        var current = Volatile.Read(ref cached);
        if (current != null && now - current.Item1 < RefreshInterval && now >= current.Item1) return current.Item2;
        var maxAge = TimeSpan.FromSeconds(settings.ReportMaxAgeSeconds > 0 ? settings.ReportMaxAgeSeconds : 10);
        var latest = bridge?.Latest;
        var state = DashboardState.Create(latest, now, maxAge, bridge?.Status ?? "Not started");
        // Fixture mode tests the overlay without LMU, so it doesn't wait for the game.
        visibility.Observe(OverlayVisibility.ScreenOf(latest, now, maxAge), latest?.Mode == "Fixture" || lmu.IsRunning(now));
        Volatile.Write(ref cached, Tuple.Create(now, state));
        return state;
    }
}
