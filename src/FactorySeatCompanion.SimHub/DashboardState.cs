using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FactorySeatCompanion.SimHub;

public sealed class DashboardRow
{
    public static readonly DashboardRow Hidden = new DashboardRow(false, "", "", "", "", "", "", "");

    public DashboardRow(bool visible, string id, string label, string required, string current, string status, string reason,
        string hint)
    {
        Visible = visible; Id = id; Label = label; Required = required; Current = current; Status = status; Reason = reason;
        Hint = hint;
    }

    public bool Visible { get; }
    public string Id { get; }
    public string Label { get; }
    public string Required { get; }
    public string Current { get; }
    public string Status { get; }
    public string Reason { get; }
    // What the overlay prints: NotApplicable reads as "Any", the briefing's wording.
    // The custom team identity row can't be checked at all, so it says so rather than "Any".
    public string StatusText => Status == "NotApplicable" ? (Id == "customTeam" ? "Not checkable" : "Any")
        : Status == "Manual" ? "Check" : Status;
    // Checklist mark: tick, cross, dash for Any, a box for settings checked by hand, and an empty circle while
    // the value is unknown,
    // so the overlay still reads as a list of settings to set when LMU can't be read.
    public string Icon => !Visible ? "" : Status switch
    {
        "Match" => "\u2713",
        "Mismatch" => "\u2717",
        "NotApplicable" => "\u2013",
        "Manual" => "\u2610",
        _ => "\u25CB"
    };
    // One line telling the driver what to change; only on a fresh Mismatch or Manual row.
    public string Hint { get; }
}

// Immutable view of the latest report as flat SimHub properties. Readiness fails closed:
// a missing, stale, future-dated or overflowing report is never READY TO RACE.
public sealed class DashboardState
{
    // Eleven baseline rows plus the optional custom-team row, with one spare.
    public const int RowSlots = 13;
    public const string ManualCheckText =
        "Can't read LMU's settings, so nothing here is verified. Check each setting in LMU's menu by hand against ROUND NEEDS.";

    private DashboardState(bool ready, string overall, string headline, string mode, string briefing, string lmu,
        double? age, string bridge, string diagnostics, IReadOnlyList<DashboardRow> rows, string manualCheck = "")
    {
        ManualCheck = manualCheck;
        ReadyToRace = ready; OverallStatus = overall; Headline = headline; Mode = mode; BriefingStatus = briefing;
        LmuStatus = lmu; ReportAgeSeconds = age; BridgeStatus = bridge; Diagnostics = diagnostics; Rows = rows;
        Hints = string.Join(" \u00B7 ", rows.Where(r => r.Visible && r.Hint.Length > 0).Select(r => r.Hint));
    }

    public bool ReadyToRace { get; }
    public string OverallStatus { get; }
    public string Headline { get; }
    public string Mode { get; }
    public string BriefingStatus { get; }
    public string LmuStatus { get; }
    public double? ReportAgeSeconds { get; }
    public string BridgeStatus { get; }
    public string Diagnostics { get; }
    // Every row hint on one line, for the overlay's fix line.
    public string Hints { get; }
    // Set when the round is known but LMU's settings can't be read: the rows become a checklist to work by hand.
    public string ManualCheck { get; }
    public IReadOnlyList<DashboardRow> Rows { get; }

    public static DashboardState Create(CompanionMessage? message, DateTimeOffset now, TimeSpan maxAge, string bridgeStatus)
    {
        var report = message?.Report;
        if (report is null)
            return new DashboardState(false, "NOT VERIFIED", "Waiting for Factory Seat Companion", message?.Mode ?? "",
                "Unknown", "Unknown", null, bridgeStatus, bridgeStatus, Enumerable.Repeat(DashboardRow.Hidden, RowSlots).ToList());

        var age = now - report.ObservedAt;
        // Allow one second of clock skew, matching SettingsValidator.
        bool fresh = age <= maxAge && age >= TimeSpan.FromSeconds(-1);
        var checks = report.Checks ?? new List<CompanionCheck>();
        var diagnostics = (report.Diagnostics ?? new List<string>()).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        if (!fresh)
            diagnostics.Insert(0, string.Format(CultureInfo.InvariantCulture,
                "Report observed {0:0}s ago; limit is {1:0}s.", age.TotalSeconds, maxAge.TotalSeconds));
        if (checks.Count > RowSlots)
            diagnostics.Insert(0, $"{checks.Count} checks exceed the {RowSlots} dashboard rows.");

        var rows = new List<DashboardRow>(RowSlots);
        foreach (var check in checks.Take(RowSlots))
            rows.Add(fresh
                ? new DashboardRow(true, check.Id, check.Label, check.Required, check.Current, check.Status, check.Reason ?? "",
                    check.Status is "Mismatch" or "Manual" ? check.Hint ?? "" : "")
                : new DashboardRow(true, check.Id, check.Label, check.Required, "Unknown", "Unknown", "Report is stale.", ""));
        while (rows.Count < RowSlots) rows.Add(DashboardRow.Hidden);

        bool ready = fresh && report.ReadyToRace && checks.Count > 0 && checks.Count <= RowSlots &&
            checks.All(c => c.Status == "Match" || c.Status == "NotApplicable" || c.Status == "Manual");
        string overall = ready ? "READY TO RACE"
            : fresh && report.OverallStatus == "SETUP MISMATCH" ? "SETUP MISMATCH" : "NOT VERIFIED";
        // The briefing gives the values to set, so the rows still help when LMU's side is unreadable or the report is old.
        bool manual = report.BriefingStatus == "Available" && (!fresh || report.LmuStatus != "Available") && checks.Count > 0;
        // Factory Seat shows career names in capitals.
        string round = report.Round is int r ? "Round " + r.ToString(CultureInfo.InvariantCulture) : "Round ?";
        return new DashboardState(ready, overall, $"{(report.Career ?? "").ToUpperInvariant()} / {report.Event} / {round}", message!.Mode,
            fresh ? report.BriefingStatus : "Stale", fresh ? report.LmuStatus : "Stale", age.TotalSeconds,
            bridgeStatus, string.Join(" | ", diagnostics), rows, manual ? ManualCheckText : "");
    }
}
