using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactorySeatCompanion.Core;

public enum CheckStatus { Match, Mismatch, Unknown, NotApplicable }
public enum SourceStatus { Available, Unavailable, Invalid, Stale, UnsafeState }

// DEVELOPMENT CONTRACT ONLY. Replace the reader adapter when Factory Seat supplies its schema.
public sealed record ProvisionalBriefing
{
    public string? Contract { get; init; }
    public int? SchemaVersion { get; init; }
    public DateTimeOffset? GeneratedAt { get; init; }
    public string? CareerName { get; init; }
    public string? EventName { get; init; }
    public int? RoundNumber { get; init; }
    public string? State { get; init; }
    public string? TrackFolder { get; init; }
    public string? LayoutFile { get; init; }
    public double? RaceMinutes { get; init; }
    public double? FuelMultiplier { get; init; }
    public double? TireMultiplier { get; init; }
    public double? TimeScale { get; init; }
    public string? StartTime { get; init; }
    // Explicit omission policies: missing is Unknown, never implicitly optional.
    public bool? StartTimeRequired { get; init; }
    public CarRequirement? Car { get; init; }
}

public sealed record CarRequirement
{
    // Effective car for this round, including guest drives; not necessarily the season car.
    public string? Folder { get; init; }
    public string? Class { get; init; }
    public string? Number { get; init; }
    public string[]? PermittedNumbers { get; init; }
    public bool? AnyNumber { get; init; }
    public bool? CustomTeam { get; init; }
    public string? Team { get; init; }
    public bool? TeamRequired { get; init; }
    public string? LiveryName { get; init; }
}

public sealed record BriefingRead(SourceStatus Status, ProvisionalBriefing? Briefing, string Diagnostic);
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
    public double BriefingMaxAgeMinutes { get; init; } = 120;
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
        if (!double.IsFinite(BriefingMaxAgeMinutes) || BriefingMaxAgeMinutes <= 0 ||
            !double.IsFinite(SnapshotMaxAgeSeconds) || SnapshotMaxAgeSeconds <= 0)
            throw new InvalidDataException("Freshness limits must be positive finite values.");
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
