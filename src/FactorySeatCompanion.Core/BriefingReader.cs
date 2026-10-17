using System.Text.Json;

namespace FactorySeatCompanion.Core;

public interface IFactorySeatReader
{
    Task<BriefingRead> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

// Reads Factory Seat's briefing.json (format "factory-seat-briefing", version 1).
// isFactorySeatRunning covers a crash leaving "open" true; tests substitute it.
public sealed class FactorySeatBriefingReader(string path, Func<bool>? isFactorySeatRunning = null) : IFactorySeatReader
{
    public const string FormatName = "factory-seat-briefing";
    public const int SupportedVersion = 1;
    public const int MaximumBytes = 1024 * 1024;

    // Factory Seat writes next to its saves: %AppData%\LmuCareer, or LMUCAREER_HOME when set.
    public static string DefaultPath
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("LMUCAREER_HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LmuCareer");
            return Path.Combine(home, "briefing.json");
        }
    }

    // Read anew each cycle: catches atomic replacements and partial writes without watcher races.
    // No last-good fallback; invalid or missing files revoke readiness immediately.
    public async Task<BriefingRead> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length > MaximumBytes) return new(SourceStatus.Invalid, null, "Briefing exceeds 1 MiB.");
            var json = await BoundedJson.ReadAsync(stream, MaximumBytes, cancellationToken);
            return Parse(json, isFactorySeatRunning ?? FactorySeatProcess.IsRunning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(SourceStatus.Unavailable, null, $"Briefing read failed: {ex.Message}");
        }
    }

    public static BriefingRead Parse(string json, Func<bool> isFactorySeatRunning)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (JsonRead.HasDuplicateProperties(root))
                return new(SourceStatus.Invalid, null, "Duplicate briefing properties.");
            if (JsonRead.Text(JsonRead.Property(root, "format")) != FormatName)
                return new(SourceStatus.Invalid, null, $"Not a Factory Seat briefing (format must be \"{FormatName}\").");
            var version = ReadInt(JsonRead.Property(root, "version"));
            if (version != SupportedVersion)
                return new(SourceStatus.Invalid, null, version > SupportedVersion
                    ? $"Briefing version {version} is newer than this companion supports ({SupportedVersion}); update the companion."
                    : "Missing or invalid briefing version.");
            var open = ReadBool(JsonRead.Property(root, "open"));
            if (open is null) return new(SourceStatus.Invalid, null, "Briefing is missing the open flag.");
            var round = JsonRead.Property(root, "round");
            if (round is null || round.Value.ValueKind == JsonValueKind.Null)
                return new(SourceStatus.Unavailable, null, "No round to set up (between seasons).");
            if (round.Value.ValueKind != JsonValueKind.Object)
                return new(SourceStatus.Invalid, null, "Briefing round is not an object.");

            var requirement = Requirement(JsonRead.Property(root, "career"), round.Value);
            // The file outlives the session, so a closed career is shown but never verified.
            if (open == false)
                return new(SourceStatus.Stale, requirement, "Career is not open in Factory Seat; showing the last briefing viewed.");
            if (!isFactorySeatRunning())
                return new(SourceStatus.Stale, requirement, "Factory Seat is not running; the briefing is from its last session.");
            if (requirement.State is not ("Upcoming" or "Armed"))
                return new(SourceStatus.Invalid, requirement, "Round must be Upcoming or Armed.");
            return new(SourceStatus.Available, requirement, "Factory Seat briefing loaded.");
        }
        catch (JsonException ex) { return new(SourceStatus.Invalid, null, $"Invalid briefing JSON: {ex.Message}"); }
    }

    private static RoundRequirement Requirement(JsonElement? career, JsonElement round)
    {
        var track = JsonRead.Property(round, "track");
        var car = JsonRead.Property(round, "car");
        var settings = JsonRead.Property(round, "settings");
        var startTime = JsonRead.Property(settings, "startTime");
        var number = JsonRead.Property(car, "number");
        var team = JsonRead.Text(JsonRead.Property(car, "team"));
        // 1.2: pace is the race's advice; practice carries the same step before any race has counted.
        var pace = JsonRead.Property(round, "pace");
        var paceStep = ReadInt(JsonRead.Property(pace, "step"));
        var practiceStep = ReadInt(JsonRead.Property(JsonRead.Property(round, "practice"), "aiStrengthStep"));
        var stakes = JsonRead.Property(JsonRead.Property(round, "stakes"), "lines");
        return new RoundRequirement
        {
            CareerName = ReadString(JsonRead.Property(career, "name")),
            EventName = ReadString(JsonRead.Property(JsonRead.Property(round, "event"), "name")),
            RoundNumber = ReadInt(JsonRead.Property(round, "number")),
            RoundCount = ReadInt(JsonRead.Property(round, "of")),
            State = ReadString(JsonRead.Property(round, "state")),
            Guest = ReadBool(JsonRead.Property(round, "guest")),
            TrackFolder = ReadString(JsonRead.Property(track, "folder")),
            LayoutFile = ReadString(JsonRead.Property(track, "layoutFile")),
            TrackName = ReadString(JsonRead.Property(track, "name")),
            LayoutName = ReadString(JsonRead.Property(track, "layoutName")),
            RaceMinutes = ReadDouble(JsonRead.Property(settings, "raceMinutes")),
            // Factory Seat uses LMU's factors: 1 is Real/Normal, otherwise xN.
            FuelMultiplier = ReadDouble(JsonRead.Property(settings, "fuelUsage")),
            TireMultiplier = ReadDouble(JsonRead.Property(settings, "tyreWear")),
            TimeScale = ReadDouble(JsonRead.Property(settings, "timeScale")),
            StartTime = ReadString(startTime),
            StartTimeDefault = startTime?.ValueKind switch
            {
                JsonValueKind.Null => true,
                JsonValueKind.String => false,
                _ => null
            },
            Car = car is { ValueKind: JsonValueKind.Object } ? new CarRequirement
            {
                Folder = ReadString(JsonRead.Property(car, "folder")),
                Name = FirstString(JsonRead.Property(car, "carTypes")) ?? ReadString(JsonRead.Property(car, "name")),
                Class = ReadString(JsonRead.Property(car, "class")),
                Number = JsonRead.Text(number),
                AnyNumber = number?.ValueKind switch
                {
                    JsonValueKind.Null => true,
                    JsonValueKind.String or JsonValueKind.Number => false,
                    _ => null
                },
                CustomTeam = ReadBool(JsonRead.Property(car, "customTeam")),
                Team = string.IsNullOrWhiteSpace(team) ? null : team
            } : null,
            AiStrengthStep = paceStep ?? practiceStep,
            AiStrengthSource = paceStep is not null ? ReadString(JsonRead.Property(pace, "source")) : practiceStep is not null ? "Practice" : null,
            AiStrengthVisits = paceStep is not null ? ReadInt(JsonRead.Property(pace, "visits")) : null,
            Stakes = stakes?.ValueKind == JsonValueKind.Array
                ? stakes.Value.EnumerateArray().Select(l => ReadString(l)).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim()).ToArray()
                : []
        };
    }

    private static string? ReadString(JsonElement? value) =>
        value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    private static string? FirstString(JsonElement? value) =>
        value?.ValueKind == JsonValueKind.Array && value.Value.GetArrayLength() > 0 ? ReadString(value.Value[0]) : null;
    private static bool? ReadBool(JsonElement? value) => value?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
    private static double? ReadDouble(JsonElement? value) =>
        value?.ValueKind == JsonValueKind.Number && value.Value.TryGetDouble(out var number) ? number : null;
    private static int? ReadInt(JsonElement? value) =>
        value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) ? number : null;
}

public static class FactorySeatProcess
{
    public const string ProcessName = "FactorySeat";

    public static bool IsRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName(ProcessName);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }
}

internal static class BoundedJson
{
    public static async Task<string> ReadAsync(Stream stream, int maximumBytes, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > maximumBytes) throw new IOException("JSON response exceeds size limit.");
            buffer.Write(chunk, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
    }
}
