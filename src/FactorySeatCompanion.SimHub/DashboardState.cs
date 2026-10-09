using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FactorySeatCompanion.SimHub;

public sealed class DashboardRow
{
    public static readonly DashboardRow Hidden = new DashboardRow(false, "", "", "", "", "", "");

    public DashboardRow(bool visible, string id, string label, string required, string current, string status, string reason)
    {
        Visible = visible; Id = id; Label = label; Required = required; Current = current; Status = status; Reason = reason;
    }

    public bool Visible { get; }
    public string Id { get; }
    public string Label { get; }
    public string Required { get; }
    public string Current { get; }
    public string Status { get; }
    public string Reason { get; }
}

// Immutable view of the latest report as flat SimHub properties. Readiness fails closed:
// a missing, stale, future-dated or overflowing report is never READY TO RACE.
public sealed class DashboardState
{
    // Eleven baseline rows plus the optional custom-team row, with one spare.
    public const int RowSlots = 13;

    private DashboardState(bool ready, string overall, string headline, string mode, string briefing, string lmu,
        double? age, string bridge, string diagnostics, IReadOnlyList<DashboardRow> rows)
    {
        ReadyToRace = ready; OverallStatus = overall; Headline = headline; Mode = mode; BriefingStatus = briefing;
        LmuStatus = lmu; ReportAgeSeconds = age; BridgeStatus = bridge; Diagnostics = diagnostics; Rows = rows;
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
                ? new DashboardRow(true, check.Id, check.Label, check.Required, check.Current, check.Status, check.Reason ?? "")
                : new DashboardRow(true, check.Id, check.Label, check.Required, "Unknown", "Unknown", "Report is stale."));
        while (rows.Count < RowSlots) rows.Add(DashboardRow.Hidden);

        bool ready = fresh && report.ReadyToRace && checks.Count > 0 && checks.Count <= RowSlots &&
            checks.All(c => c.Status == "Match" || c.Status == "NotApplicable");
        string overall = ready ? "READY TO RACE"
            : fresh && report.OverallStatus == "SETUP MISMATCH" ? "SETUP MISMATCH" : "NOT VERIFIED";
        string round = report.Round is int r ? "Round " + r.ToString(CultureInfo.InvariantCulture) : "Round ?";
        return new DashboardState(ready, overall, $"{report.Career} / {report.Event} / {round}", message!.Mode,
            fresh ? report.BriefingStatus : "Stale", fresh ? report.LmuStatus : "Stale", age.TotalSeconds,
            bridgeStatus, string.Join(" | ", diagnostics), rows);
    }
}
