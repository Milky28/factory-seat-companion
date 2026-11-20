using System;
using System.Collections.Generic;

namespace FactorySeatCompanion.SimHub;

// Mirrors one line of `FactorySeatCompanion.Cli --jsonl`. Enums arrive as strings.
// Kept free of SimHub and serializer attributes so the tests can compile it under net8.0.
public sealed class CompanionMessage
{
    public string Mode { get; set; } = "";
    // Menu, Loading, InSession or Unknown; see LmuScreenClassifier in the core.
    public string Screen { get; set; } = "";
    public CompanionReport? Report { get; set; }
}

public sealed class CompanionReport
{
    public string Career { get; set; } = "";
    public string Event { get; set; } = "";
    public int? Round { get; set; }
    public bool ReadyToRace { get; set; }
    public string OverallStatus { get; set; } = "";
    public string BriefingStatus { get; set; } = "";
    public string LmuStatus { get; set; } = "";
    public DateTimeOffset ObservedAt { get; set; }
    public List<CompanionCheck> Checks { get; set; } = new List<CompanionCheck>();
    public List<string> Diagnostics { get; set; } = new List<string>();
    // The briefing's stakes lines, most important first.
    public List<string> Stakes { get; set; } = new List<string>();
    // true once the race weekend is started in Factory Seat, false while the round is Upcoming.
    public bool? WeekendStarted { get; set; }
    public bool TestDay { get; set; }
    // A to-do that isn't about one LMU setting, such as starting the race weekend.
    public string? Hint { get; set; }
    // "imsa" when the round counts for IMSA; null for the season's own championship.
    public string? Series { get; set; }
    // The briefing's pit plan; null on a test day.
    public CompanionPitPlan? PitPlan { get; set; }
    // The championship standings in numbers; null for a guest drive or an older Factory Seat.
    public CompanionStandings? Standings { get; set; }
}

public sealed class CompanionPitPlan
{
    public int? Stints { get; set; }
    public double? StintMinutes { get; set; }
    public int? Stops { get; set; }
}

public sealed class CompanionStandings
{
    public int? Position { get; set; }
    public double? Points { get; set; }
    public int? TargetPosition { get; set; }
    public CompanionRival? Ahead { get; set; }
    public CompanionRival? Behind { get; set; }
}

public sealed class CompanionRival
{
    public string? Name { get; set; }
    public int? Position { get; set; }
    public double? Gap { get; set; }
}

public sealed class CompanionCheck
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Required { get; set; } = "";
    public string Current { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
    public string? Hint { get; set; }
}
