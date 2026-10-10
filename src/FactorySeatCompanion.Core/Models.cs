using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactorySeatCompanion.Core;

public enum CheckStatus { Match, Mismatch, Unknown, NotApplicable }
public enum SourceStatus { Available, Unavailable, Invalid, Stale, UnsafeState }

// Normalized requirements for the next round, read from Factory Seat's briefing.json.
// Null means the briefing did not say; it is never treated as unrestricted.
public sealed record RoundRequirement
{
    public string? CareerName { get; init; }
    public string? EventName { get; init; }
    public int? RoundNumber { get; init; }
    public int? RoundCount { get; init; }
    public string? State { get; init; }
    public bool? Guest { get; init; }
    public string? TrackFolder { get; init; }
    public string? LayoutFile { get; init; }
    public double? RaceMinutes { get; init; }
    public double? FuelMultiplier { get; init; }
    public double? TireMultiplier { get; init; }
    public double? TimeScale { get; init; }
    public string? StartTime { get; init; }
    // true: startTime was explicitly null, meaning LMU's default start time.
    public bool? StartTimeDefault { get; init; }
    public CarRequirement? Car { get; init; }
}

public sealed record CarRequirement
{
    // Effective car for this round, including guest drives; not necessarily the season car.
    public string? Folder { get; init; }
    public string? Class { get; init; }
    public string? Number { get; init; }
    // true: number was explicitly null, so any livery will do and the first race decides.
    public bool? AnyNumber { get; init; }
    public bool? CustomTeam { get; init; }
    // Compared only when present; Factory Seat gives null when the team is not fixed.
    public string? Team { get; init; }
}

public sealed record BriefingRead(SourceStatus Status, RoundRequirement? Briefing, string Diagnostic);
public sealed record LmuSnapshot
{
    public DateTimeOffset ObservedAt { get; init; }
    public SourceStatus Status { get; init; } = SourceStatus.Available;
    public string? TrackFolder { get; init; }
    public string? SceneDescription { get; init; }
    public string? CarFolder { get; init; }
    public string[]? Classes { get; init; }
    public string? Number { get; init; }
    public string? Team { get; init; }
    public string? LiveryName { get; init; }
    public double? RaceMinutes { get; init; }
    public double? FuelMultiplier { get; init; }
    public double? TireMultiplier { get; init; }
    public double? TimeScale { get; init; }
    public string? StartTime { get; init; }
    // From the navigation reads of the same poll; decides when the overlay shows, never readiness.
    public LmuScreen Screen { get; init; } = LmuScreen.Unknown;
    public string[] Diagnostics { get; init; } = [];
}

public sealed record SettingCheck(string Id, string Label, string Required, string Current,
    CheckStatus Status, string? Reason = null);
public sealed record ValidationReport(string Career, string Event, int? Round, bool ReadyToRace,
    string OverallStatus, SourceStatus BriefingStatus, SourceStatus LmuStatus,
    DateTimeOffset ObservedAt, IReadOnlyList<SettingCheck> Checks, IReadOnlyList<string> Diagnostics);

public sealed record LayoutMapping(string TrackFolder, string SceneDescription, string LayoutFile);
public sealed record CompanionOptions
{
    public double PollSeconds { get; init; } = 1;
    public double RequestTimeoutSeconds { get; init; } = 2;
    public double SnapshotMaxAgeSeconds { get; init; } = 10;
    public string NavigationStatePointer { get; init; } = "/state";
    // Empty until actual navigation responses have been captured. Fail closed.
    public string[] SafeSetupStates { get; init; } = [];
    public LayoutMapping[] LayoutMappings { get; init; } = [];

    public void Validate()
    {
        if (!double.IsFinite(PollSeconds) || PollSeconds < 1 || PollSeconds > 60)
            throw new InvalidDataException("pollSeconds must be between 1 and 60.");
        if (!double.IsFinite(RequestTimeoutSeconds) || RequestTimeoutSeconds < 0.1 || RequestTimeoutSeconds > 10)
            throw new InvalidDataException("requestTimeoutSeconds must be between 0.1 and 10.");
        if (!double.IsFinite(SnapshotMaxAgeSeconds) || SnapshotMaxAgeSeconds <= 0)
            throw new InvalidDataException("snapshotMaxAgeSeconds must be a positive finite value.");
        if (string.IsNullOrEmpty(NavigationStatePointer) || !NavigationStatePointer.StartsWith('/') ||
            SafeSetupStates is null || LayoutMappings is null ||
            SafeSetupStates.Any(s => string.IsNullOrWhiteSpace(s) || s.Contains("replay", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid navigation policy or mappings.");
        if (LayoutMappings.Any(m => m is null || string.IsNullOrWhiteSpace(m.TrackFolder) ||
            string.IsNullOrWhiteSpace(m.SceneDescription) || string.IsNullOrWhiteSpace(m.LayoutFile)))
            throw new InvalidDataException("Layout mappings must contain all three identifiers.");
    }
}

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
