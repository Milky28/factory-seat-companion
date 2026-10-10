using System.Globalization;
using System.Text.Json;
using FactorySeatCompanion.Core;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("Factory Seat Companion: Phase 1 diagnostic CLI");
        Console.WriteLine("[--briefing <path>] [--fixtures <directory>] [--config <path>] [--once] [--json | --jsonl]");
        Console.WriteLine($"--briefing defaults to {FactorySeatBriefingReader.DefaultPath}");
        Console.WriteLine("--poll-seconds <1..60> --timeout-seconds <0.1..10>");
        Console.WriteLine("--inspect-navigation: GET /navigation/state once without garage access (no briefing needed).");
        Console.WriteLine("--jsonl: one compact JSON report per line (SimHub plugin bridge).");
        Console.WriteLine("--exit-when-stdin-closes: stop when the parent process closes standard input.");
        Console.WriteLine("Exit codes with --once: 0=verified, 2=not verified/mismatch, 1=usage/configuration error.");
        return 0;
    }
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    try
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        string[] validFlags = ["--once", "--json", "--jsonl", "--inspect-navigation", "--exit-when-stdin-closes"];
        string[] validValues = ["--briefing", "--fixtures", "--config", "--poll-seconds", "--timeout-seconds"];
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
        if (flags.Contains("--json") && flags.Contains("--jsonl")) throw new ArgumentException("Use either --json or --jsonl.");
        if (flags.Contains("--exit-when-stdin-closes"))
        {
            // The SimHub plugin keeps stdin open; EOF means the host exited or stopped the bridge.
            _ = Task.Run(async () =>
            {
                while (await Console.In.ReadLineAsync() is not null) { }
                cancellation.Cancel();
            });
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
            RequestTimeoutSeconds = Override("--timeout-seconds", options.RequestTimeoutSeconds)
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
        ILmuSource source = values.TryGetValue("--fixtures", out var fixturePath)
            ? new FixtureLmuSource(fixturePath) : new LmuApiClient(http, options);
        // Fixture mode is synthetic end to end, so it does not require Factory Seat to be running.
        IFactorySeatReader reader = new FactorySeatBriefingReader(values.GetValueOrDefault("--briefing", FactorySeatBriefingReader.DefaultPath),
            source is FixtureLmuSource ? () => true : null);
        var validator = new SettingsValidator(options);
        bool structured = flags.Contains("--json") || flags.Contains("--jsonl");
        var jsonOptions = flags.Contains("--jsonl") ? new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = false } : JsonDefaults.Options;
        if (source is FixtureLmuSource && !structured) Console.WriteLine("FIXTURE MODE: synthetic observations, not live verification.");
        while (!cancellation.IsCancellationRequested)
        {
            var live = await source.PollAsync(cancellation.Token);
            // Read the briefing after network requests so an event change during polling is noticed.
            var briefing = await reader.ReadAsync(DateTimeOffset.UtcNow, cancellation.Token);
            var report = validator.Validate(briefing, live, DateTimeOffset.UtcNow);
            if (structured) Console.WriteLine(JsonSerializer.Serialize(new
            {
                mode = source is FixtureLmuSource ? "Fixture" : "Live", screen = live.Screen, report
            }, jsonOptions));
            else Render(report, live.Screen);
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

static void Render(ValidationReport report, LmuScreen screen)
{
    Console.WriteLine($"\n{report.Career} / {report.Event} / Round {report.Round} | {report.OverallStatus}");
    Console.WriteLine($"Briefing: {report.BriefingStatus}; LMU: {report.LmuStatus} ({screen}); observation: {report.ObservedAt:O}");
    foreach (var check in report.Checks)
    {
        if (!Console.IsOutputRedirected) Console.ForegroundColor = check.Status switch
        {
            CheckStatus.Match => ConsoleColor.Green,
            CheckStatus.Mismatch => ConsoleColor.Red,
            CheckStatus.NotApplicable => ConsoleColor.DarkGray,
            _ => ConsoleColor.Yellow
        };
        Console.WriteLine($"{SettingCheck.DisplayStatus(check.Status),-13} {check.Label,-22} required={check.Required} | LMU={check.Current}");
        if (check.Hint is not null) Console.WriteLine($"              Fix: {check.Hint}");
        if (check.Reason is not null) Console.WriteLine($"              {check.Reason}");
        if (!Console.IsOutputRedirected) Console.ResetColor();
    }
    foreach (var diagnostic in report.Diagnostics) Console.WriteLine($"  {diagnostic}");
}
