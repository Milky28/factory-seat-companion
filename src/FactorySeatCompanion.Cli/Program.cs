using System.Globalization;
using System.Text.Json;
using FactorySeatCompanion.Core;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("Factory Seat Companion: Phase 1 diagnostic CLI (development contract only)");
        Console.WriteLine("--briefing <path> [--fixtures <directory>] [--config <path>] [--once] [--json]");
        Console.WriteLine("--poll-seconds <1..60> --timeout-seconds <0.1..10> --briefing-max-age-minutes <minutes>");
        Console.WriteLine("--inspect-navigation: GET /navigation/state once without garage access (no briefing needed).");
        Console.WriteLine("Exit codes with --once: 0=verified, 2=not verified/mismatch, 1=usage/configuration error.");
        return 0;
    }
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    try
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        string[] validFlags = ["--once", "--json", "--inspect-navigation"];
        string[] validValues = ["--briefing", "--fixtures", "--config", "--poll-seconds", "--timeout-seconds", "--briefing-max-age-minutes"];
        for (int i = 0; i < args.Length; i++)
        {
            if (validFlags.Contains(args[i]))
            {
                if (!flags.Add(args[i])) throw new ArgumentException($"Duplicate option {args[i]}.");
            }
            else if (validValues.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                if (!values.TryAdd(args[i], args[++i])) throw new ArgumentException("Duplicate option.");
            }
            else throw new ArgumentException($"Unknown option or missing value: {args[i]}.");
        }
        var options = values.TryGetValue("--config", out var configPath)
            ? JsonSerializer.Deserialize<CompanionOptions>(await File.ReadAllTextAsync(configPath, cancellation.Token), JsonDefaults.Options)
                ?? throw new InvalidDataException("Empty configuration.")
            : new CompanionOptions();
        double Override(string name, double fallback) => values.TryGetValue(name, out var value)
            ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
        options = options with
        {
            PollSeconds = Override("--poll-seconds", options.PollSeconds),
            RequestTimeoutSeconds = Override("--timeout-seconds", options.RequestTimeoutSeconds),
            BriefingMaxAgeMinutes = Override("--briefing-max-age-minutes", options.BriefingMaxAgeMinutes)
        };
        options.Validate();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        if (flags.Contains("--inspect-navigation"))
        {
            // Inspection is read-only and requests no garage/settings endpoints.
            Console.WriteLine(await new LmuApiClient(http, options).InspectNavigationAsync(cancellation.Token));
            return 0;
        }
        if (!values.TryGetValue("--briefing", out var briefingPath)) throw new ArgumentException("--briefing is required.");
        IFactorySeatReader reader = new ProvisionalJsonBriefingReader(briefingPath, TimeSpan.FromMinutes(options.BriefingMaxAgeMinutes));
        ILmuSource source = values.TryGetValue("--fixtures", out var fixturePath)
            ? new FixtureLmuSource(fixturePath) : new LmuApiClient(http, options);
        var validator = new SettingsValidator(options);
        if (source is FixtureLmuSource && !flags.Contains("--json")) Console.WriteLine("FIXTURE MODE: synthetic observations, not live verification.");
        while (!cancellation.IsCancellationRequested)
        {
            var live = await source.PollAsync(cancellation.Token);
            // Read the briefing after network requests so an event change during polling is noticed.
            var briefing = await reader.ReadAsync(DateTimeOffset.UtcNow, cancellation.Token);
            var report = validator.Validate(briefing, live, DateTimeOffset.UtcNow);
            if (flags.Contains("--json")) Console.WriteLine(JsonSerializer.Serialize(new
            {
                mode = source is FixtureLmuSource ? "Fixture" : "Live", report
            }, JsonDefaults.Options));
            else Render(report);
            if (flags.Contains("--once")) return report.ReadyToRace ? 0 : 2;
            // Fixed delay after completion, no concurrent polls and no backlog on slow requests.
            await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), cancellation.Token);
        }
        return 0;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 0; }
    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidDataException or
        IOException or UnauthorizedAccessException or JsonException or HttpRequestException or OperationCanceledException)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static void Render(ValidationReport report)
{
    Console.WriteLine($"\n{report.Career} / {report.Event} / Round {report.Round} | {report.OverallStatus}");
    Console.WriteLine($"Briefing: {report.BriefingStatus}; LMU: {report.LmuStatus}; observation: {report.ObservedAt:O}");
    foreach (var check in report.Checks)
    {
        if (!Console.IsOutputRedirected) Console.ForegroundColor = check.Status switch
        {
            CheckStatus.Match => ConsoleColor.Green,
            CheckStatus.Mismatch => ConsoleColor.Red,
            _ => ConsoleColor.Yellow
        };
        Console.WriteLine($"{check.Status,-13} {check.Label,-22} required={check.Required} | LMU={check.Current}");
        if (check.Reason is not null) Console.WriteLine($"              {check.Reason}");
        if (!Console.IsOutputRedirected) Console.ResetColor();
    }
    foreach (var diagnostic in report.Diagnostics) Console.WriteLine($"  {diagnostic}");
}
