using System.Globalization;

namespace FactorySeatCompanion.Core;

public sealed class SettingsValidator(CompanionOptions options)
{
    public ValidationReport Validate(BriefingRead read, LmuSnapshot live, DateTimeOffset now)
    {
        var b = read.Briefing;
        var car = b?.Car;
        var checks = new List<SettingCheck>();
        string? mappingHelp = null;
        var lmuStatus = live.Status;
        if (now - live.ObservedAt > TimeSpan.FromSeconds(options.SnapshotMaxAgeSeconds) || live.ObservedAt > now.AddSeconds(1))
            lmuStatus = SourceStatus.Stale;
        bool valid = read.Status == SourceStatus.Available && lmuStatus == SourceStatus.Available;
        void Add(string id, string label, string? required, string? current, bool? matches, bool notApplicable = false,
            string? reason = null, string? hint = null, string? name = null, bool manual = false)
        {
            var status = !valid ? CheckStatus.Unknown : notApplicable ? CheckStatus.NotApplicable : manual ? CheckStatus.Manual :
                matches is null ? CheckStatus.Unknown : matches.Value ? CheckStatus.Match : CheckStatus.Mismatch;
            // Show the briefing's display name rather than an identifier; LMU's side shares it once matched.
            var shown = Present(name) && required is not null ? name : required;
            checks.Add(new(id, label, shown ?? "Unknown", (status == CheckStatus.Match ? shown : current) ?? "Unknown", status,
                !valid ? $"Sources not current: briefing={read.Status}, LMU={lmuStatus}." : reason,
                status is CheckStatus.Mismatch or CheckStatus.Manual ? hint : null));
        }
        static bool Present(string? s) => !string.IsNullOrWhiteSpace(s);
        static bool? Same(string? a, string? c) => Present(a) && Present(c)
            ? string.Equals(a!.Trim(), c!.Trim(), StringComparison.OrdinalIgnoreCase) : null;
        static string? Display(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture);
        void Number(string id, string label, double? required, double? current, string hint, bool positive = false)
        {
            bool Sane(double? v) => v is { } n && double.IsFinite(n) && (positive ? n > 0 : n >= 0);
            Add(id, label, Display(required), Display(current), Sane(required) && Sane(current)
                ? Math.Abs(required!.Value - current!.Value) <= 0.001 : null, hint: hint);
        }
        // LMU's menu wording: factor 1 is Realistic (fuel, tyres) or Normal (time scale), otherwise X<n>.
        static string Factor(double? value, string one) =>
            value == 1 ? one : value == 0 ? "Off" : "X" + Display(value);

        Add("circuit", "Circuit", b?.TrackFolder, live.TrackFolder, Same(b?.TrackFolder, live.TrackFolder),
            hint: $"Select {b?.TrackName ?? b?.TrackFolder}", name: b?.TrackName);
        var layouts = options.LayoutMappings.Where(m => Same(m.TrackFolder, live.TrackFolder) == true &&
            Same(m.SceneDescription, live.SceneDescription) == true).ToArray();
        var mappedLayout = layouts.Length == 1 ? layouts[0].LayoutFile : null;
        bool? layoutMatch = !Present(b?.LayoutFile) || !Present(b?.TrackFolder) || !Present(live.TrackFolder) ? null :
            Same(b?.TrackFolder, live.TrackFolder) == false ? false : Same(b?.LayoutFile, mappedLayout);
        Add("layout", "Exact layout", b?.LayoutFile, mappedLayout ?? live.SceneDescription, layoutMatch,
            reason: mappedLayout is null ? "No unique cmpName + sceneDesc mapping. Layout display names are not used." : null,
            hint: $"Select the {b?.LayoutName ?? b?.LayoutFile} layout", name: b?.LayoutName);
        // Layouts need a mapping, which takes LMU's identifiers; the rows show names, so list them here.
        if (valid && mappedLayout is null && Present(live.TrackFolder))
            mappingHelp = $"No layout mapping for trackFolder {live.TrackFolder}, sceneDescription {live.SceneDescription ?? "Unknown"}.";
        Add("carModel", "Car model", car?.Folder, live.CarFolder, Same(car?.Folder, live.CarFolder),
            hint: $"Select the {car?.Name ?? car?.Folder}", name: car?.Name);
        bool? classMatch = Present(car?.Class) && live.Classes is { Length: > 0 }
            ? live.Classes.Contains(car!.Class!.Trim(), StringComparer.OrdinalIgnoreCase) : null;
        // LMU lists the car's own id and series beside its class; a match shows just the class.
        Add("carClass", "Car class", car?.Class, live.Classes is null ? null : string.Join(", ", live.Classes), classMatch,
            hint: $"Select a {car?.Class} car");

        // Custom-team cars may use any number; a null number means any livery until the first race decides.
        bool unrestrictedNumber = car?.AnyNumber == true || car?.CustomTeam == true;
        static string CleanNumber(string s) => s.Trim().TrimStart('#').Trim();
        bool? numberMatch = Present(car?.Number) && Present(live.Number)
            ? CleanNumber(car!.Number!) == CleanNumber(live.Number!) : null;
        Add("carNumber", "Car number", unrestrictedNumber ? "Any number" : car?.Number,
            live.Number, numberMatch, unrestrictedNumber, hint: $"Select car #{CleanNumber(car?.Number ?? "")}");
        // Factory Seat names a team only when the round fixes one. A Race Control custom team's name
        // is not in LMU's API (the car reads as e.g. "Lexus Custom Team 2026"), so it is left to the
        // custom team identity row, like the number.
        bool anyTeam = car is not null && (car.Team is null || car.CustomTeam == true);
        Add("team", "Team", car is null ? null : anyTeam ? "Any team" : car.Team, live.Team,
            Same(car?.Team, live.Team), anyTeam, hint: $"Select the {car?.Team} livery");
        // LMU's API has no custom-team marker, so a custom team round can't be confirmed. It is shown as
        // "Not checkable" rather than Unknown so a round where everything checkable matches reads READY TO RACE.
        // A briefing that doesn't say whether the team is custom stays Unknown.
        if (car?.CustomTeam == true)
            Add("customTeam", "Custom team identity", "Race Control custom team", "Not checkable", null, true,
                reason: "LMU's API has no custom-team marker; the custom team can't be confirmed.");
        else if (car?.CustomTeam is null)
            Add("customTeam", "Custom team identity", null, null, null,
                reason: "REST custom-team marker has not been established; matching a team name is insufficient.");
        // Race setup, live (2026-10-10): with finish criteria Time, RACE lengthTime tracks the menu's Race Time
        // within a second. With "% Track Time" LMU stores a fraction of 24 h and minute-only changes never reach
        // the API until the hours change or the session starts, so the driver checks it by hand; it never blocks.
        string raceTime = b?.RaceMinutes is { } m && double.IsFinite(m) && m > 0
            ? $"Set Race Time to {(int)Math.Round(m) / 60} hrs {(int)Math.Round(m) % 60} mins" : "";
        if (string.Equals(live.FinishCriteria?.Trim(), "Time", StringComparison.OrdinalIgnoreCase) || raceTime.Length == 0)
            Number("duration", "Race duration (min)", b?.RaceMinutes, live.RaceMinutes, raceTime, true);
        else
            Add("duration", "Race duration (min)", Display(b!.RaceMinutes), "Set in menu", null, manual: true,
                reason: $"LMU's race finish is '{live.FinishCriteria ?? "unknown"}', so it doesn't report Race Time changes yet.",
                hint: raceTime + " (change it once and back so LMU confirms it)");
        Number("fuel", "Fuel consumption", b?.FuelMultiplier, live.FuelMultiplier,
            $"Set Fuel Usage to {Factor(b?.FuelMultiplier, "Realistic")}");
        Number("tyres", "Tyre wear", b?.TireMultiplier, live.TireMultiplier,
            $"Set Tyre Wear to {Factor(b?.TireMultiplier, "Realistic")}");
        Number("timeScale", "Time scale", b?.TimeScale, live.TimeScale,
            $"Set Time Scale to {Factor(b?.TimeScale, "Normal")}", true);
        var startTime = Normalization.Time(b?.StartTime);
        Add("startTime", "Race start time", b?.StartTimeDefault == true ? "LMU default" : b?.StartTime,
            live.StartTime, b?.StartTimeDefault == false ? Same(startTime, Normalization.Time(live.StartTime)) : null,
            b?.StartTimeDefault == true, hint: startTime is null ? null : "Set Start Time to " +
                DateTime.ParseExact(startTime, "HH:mm", CultureInfo.InvariantCulture).ToString("h:mm tt", CultureInfo.InvariantCulture));
        // Factory Seat 1.2 advises an AI Strength change. It can't see LMU's slider and LMU doesn't report it,
        // so this is a manual row worded as a change, never a value to set. No advice yet, no row.
        if (b?.AiStrengthStep is int step)
        {
            string change = step > 0 ? $"Raise {step}%" : step < 0 ? $"Lower {-step}%" : "Keep as is";
            string? from = b.AiStrengthSource switch
            {
                "Practice" => "this weekend's practice",
                "Track" => b.AiStrengthVisits is > 0 and var v ? $"{v} earlier race{(v == 1 ? "" : "s")} here" : "earlier races here",
                "Season" => "recent rounds",
                _ => null
            };
            Add("aiStrength", "AI Strength", change, "Set in menu", null, manual: true,
                reason: "Factory Seat's advice" + (from is null ? "" : $" from {from}") + "; LMU doesn't report AI Strength.",
                hint: step == 0 ? null : $"{(step > 0 ? "Raise" : "Lower")} AI Strength {Math.Abs(step)}%");
        }

        bool metadataValid = Present(b?.CareerName) && Present(b?.EventName) && b?.RoundNumber > 0;
        bool ready = valid && metadataValid && checks.All(c => c.Status is CheckStatus.Match or CheckStatus.NotApplicable or CheckStatus.Manual);
        var diagnostics = live.Diagnostics.Concat([read.Diagnostic]).ToList();
        if (mappingHelp is not null) diagnostics.Add(mappingHelp);
        if (!metadataValid) diagnostics.Add("Briefing career/event/round metadata missing or invalid.");
        return new(b?.CareerName ?? "Unknown career", b?.EventName ?? "Unknown event", b?.RoundNumber, ready,
            ready ? "READY TO RACE" : checks.Any(c => c.Status == CheckStatus.Mismatch) ? "SETUP MISMATCH" : "NOT VERIFIED",
            read.Status, lmuStatus, live.ObservedAt, checks, diagnostics) { Stakes = b?.Stakes ?? [] };
    }
}
