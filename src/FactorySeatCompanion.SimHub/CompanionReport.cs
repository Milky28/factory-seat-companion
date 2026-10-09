using System;
using System.Collections.Generic;

namespace FactorySeatCompanion.SimHub;

// Mirrors one line of `FactorySeatCompanion.Cli --jsonl`. Enums arrive as strings.
// Kept free of SimHub and serializer attributes so the tests can compile it under net8.0.
public sealed class CompanionMessage
{
    public string Mode { get; set; } = "";
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
