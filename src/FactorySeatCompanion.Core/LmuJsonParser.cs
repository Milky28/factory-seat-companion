using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactorySeatCompanion.Core;

internal static class JsonRead
{
    public static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var result) ? result : null;

    public static string? Text(JsonElement? value) => value?.ValueKind switch
    {
        JsonValueKind.String => value.Value.GetString(),
        JsonValueKind.Number => value.Value.GetRawText(),
        _ => null
    };

    public static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(HasDuplicateProperties);
        return false;
    }

    // Permit wrappers but require a unique exact key. Never select the first of conflicting values.
    public static JsonElement? UniqueNamed(JsonElement root, string name)
    {
        var values = new List<JsonElement>();
        void Walk(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == name) values.Add(property.Value);
                    else Walk(property.Value);
                }
                if (Text(Property(element, "name")) == name || Text(Property(element, "id")) == name)
                    values.Add(element);
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var child in element.EnumerateArray()) Walk(child);
        }
        Walk(root);
        return values.Count == 1 ? values[0] : null;
    }
}

public static class Normalization
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static double? Number(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return double.TryParse(text.Trim(), NumberStyles.Float, Invariant, out var number) &&
            double.IsFinite(number) && number >= 0 ? number : null;
    }

    public static double? Multiplier(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.Equals("Realistic", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Normal", StringComparison.OrdinalIgnoreCase)) return 1;
        if (text.Equals("Off", StringComparison.OrdinalIgnoreCase)) return 0;
        var match = Regex.Match(text, @"^(?:[xX×]\s*)?([0-9]+(?:\.[0-9]+)?)(?:\s*[xX×])?$", RegexOptions.CultureInvariant);
        return match.Success ? Number(match.Groups[1].Value) : null;
    }

    public static string? Time(string? text)
    {
        string[] formats = ["H:mm", "HH:mm", "h:mm tt", "hh:mm tt"];
        return DateTime.TryParseExact(text?.Trim().ToUpperInvariant(), formats, Invariant,
            DateTimeStyles.None, out var time) ? time.ToString("HH:mm", Invariant) : null;
    }

    public static string? VehicleFolder(string? vehFile)
    {
        if (string.IsNullOrWhiteSpace(vehFile)) return null;
        var segments = vehFile.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidates = new List<string>();
        for (int i = 0; i + 3 < segments.Length; i++)
            if (segments[i].Equals("Installed", StringComparison.OrdinalIgnoreCase) &&
                segments[i + 1].Equals("Vehicles", StringComparison.OrdinalIgnoreCase) &&
                segments[i + 2] is not ("." or "..")) candidates.Add(segments[i + 2]);
        return candidates.Count == 1 ? candidates[0] : null;
    }
}

public static class LmuJsonParser
{
    public static LmuSnapshot Parse(string? sessionsJson, string? durationsJson, string? setupJson,
        DateTimeOffset observedAt)
    {
        var diagnostics = new List<string>();
        JsonDocument? Read(string? json, string endpoint)
        {
            if (json is null) { diagnostics.Add($"{endpoint}: unavailable."); return null; }
            try
            {
                var document = JsonDocument.Parse(json);
                if (JsonRead.HasDuplicateProperties(document.RootElement))
                {
                    document.Dispose();
                    diagnostics.Add($"{endpoint}: duplicate properties.");
                    return null;
                }
                return document;
            }
            catch (JsonException ex) { diagnostics.Add($"{endpoint}: invalid JSON ({ex.Message})."); return null; }
        }
        using var sessions = Read(sessionsJson, "sessions");
        using var durations = Read(durationsJson, "durations");
        using var setup = Read(setupJson, "setup");
        string? Setting(string name)
        {
            var setting = sessions is null ? null : JsonRead.UniqueNamed(sessions.RootElement, name);
            // currentValue can be an enum index or percentage. Until captured semantics are
            // known, only readable stringValue is interpreted for these four settings.
            var text = JsonRead.Text(JsonRead.Property(setting, "stringValue"));
            if (string.IsNullOrWhiteSpace(text)) diagnostics.Add($"{name}: missing/ambiguous stringValue; currentValue is not assumed to be a physical value.");
            return text;
        }
        double? Multiplier(string name)
        {
            var text = Setting(name);
            var value = Normalization.Multiplier(text);
            if (text is not null && value is null)
                diagnostics.Add($"{name}: unsupported readable value '{text[..Math.Min(text.Length, 128)]}'.");
            return value;
        }
        string? StartingTime()
        {
            var text = Setting("SESSSET_race_starting_time");
            var value = Normalization.Time(text);
            if (text is not null && value is null)
                diagnostics.Add($"Race start time: unsupported readable value '{text[..Math.Min(text.Length, 128)]}'.");
            return value;
        }

        double? raceMinutes = null;
        // Live capture: with "% Track Time" the menu showed 2h24 while RACE lengthTime was 48
        // (Bahrain) or 24 (Road Atlanta). lengthTime matched the menu only when the criteria was "Time".
        var finishCriteria = Setting("SESSSET_Finish_Criteria")?.Trim();
        bool timedRace = string.Equals(finishCriteria, "Time", StringComparison.OrdinalIgnoreCase);
        if (finishCriteria is not null && !timedRace)
            diagnostics.Add($"SESSSET_Finish_Criteria '{finishCriteria[..Math.Min(finishCriteria.Length, 128)]}': RACE lengthTime is only minutes when it is 'Time'.");
        var scheduled = durations is null ? null : JsonRead.Property(durations.RootElement, "scheduledSessions");
        if (scheduled is { ValueKind: JsonValueKind.Array } array)
        {
            var races = array.EnumerateArray().Where(s => string.Equals(
                JsonRead.Text(JsonRead.Property(s, "name"))?.Trim(), "RACE", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (races.Length == 1 && timedRace)
            {
                raceMinutes = Normalization.Number(JsonRead.Text(JsonRead.Property(races[0], "lengthTime")));
                if (raceMinutes <= 0) raceMinutes = null;
            }
        }
        if (raceMinutes is null && timedRace) diagnostics.Add("RACE lengthTime missing, invalid or ambiguous; SESSSET_race_time is never used.");

        var track = setup is null ? null : JsonRead.Property(setup.RootElement, "trackInfo");
        var car = setup is null ? null : JsonRead.Property(setup.RootElement, "selectedCar");
        var classes = JsonRead.Property(car, "classes");
        string[]? classNames = classes?.ValueKind switch
        {
            JsonValueKind.Array => classes.Value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String)
                ? classes.Value.EnumerateArray().Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0).ToArray() : null,
            JsonValueKind.String => classes.Value.GetString()?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            _ => null
        };
        return new LmuSnapshot
        {
            ObservedAt = observedAt,
            Status = sessions is null && durations is null && setup is null ? SourceStatus.Unavailable : SourceStatus.Available,
            TrackFolder = JsonRead.Text(JsonRead.Property(track, "cmpName")),
            SceneDescription = JsonRead.Text(JsonRead.Property(track, "sceneDesc")),
            CarFolder = Normalization.VehicleFolder(JsonRead.Text(JsonRead.Property(car, "vehFile"))),
            Classes = classNames,
            Number = JsonRead.Text(JsonRead.Property(car, "number")),
            Team = JsonRead.Text(JsonRead.Property(car, "team")),
            LiveryName = JsonRead.Text(JsonRead.Property(car, "liveryName")),
            RaceMinutes = raceMinutes,
            FinishCriteria = finishCriteria,
            FuelMultiplier = Multiplier("SESSSET_Fuel_Usage"),
            TireMultiplier = Multiplier("SESSSET_Tire_Wear"),
            TimeScale = Multiplier("SESSSET_race_timescale"),
            StartTime = StartingTime(),
            Diagnostics = diagnostics.ToArray()
        };
    }
}
