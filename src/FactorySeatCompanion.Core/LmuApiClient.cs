namespace FactorySeatCompanion.Core;

public interface ILmuSource
{
    Task<LmuSnapshot> PollAsync(CancellationToken cancellationToken = default);
}

public sealed class LmuApiClient : ILmuSource
{
    public static readonly Uri BaseUri = new("http://127.0.0.1:6397/");
    private readonly HttpClient http;
    private readonly CompanionOptions options;
    private readonly TimeProvider clock;

    public LmuApiClient(HttpClient http, CompanionOptions options, TimeProvider? clock = null)
    {
        options.Validate();
        this.http = http;
        this.options = options;
        this.clock = clock ?? TimeProvider.System;
    }

    public Task<string> InspectNavigationAsync(CancellationToken cancellationToken = default) =>
        ReadJsonAsync("navigation/state", cancellationToken);

    private async Task<string> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        using var response = await http.GetAsync(new Uri(BaseUri, path), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        return await BoundedJson.ReadAsync(stream, 2 * 1024 * 1024, timeout.Token);
    }

    public async Task<LmuSnapshot> PollAsync(CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        var started = clock.GetUtcNow();
        async Task<string?> Get(string path)
        {
            try
            {
                // The CLI creates the handler with redirects and proxy disabled. No write methods.
                return await ReadJsonAsync(path, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add($"{path}: request timed out.");
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                diagnostics.Add($"{path}: {ex.Message}");
                return null;
            }
        }
        LmuSnapshot Blocked(SourceStatus status, string diagnostic) => new()
        {
            ObservedAt = started, Status = status, Diagnostics = diagnostics.Append(diagnostic).ToArray()
        };
        var navigationJson = await Get("navigation/state");
        var before = NavigationPolicy.Evaluate(navigationJson, options);
        if (!before.Safe) return Blocked(navigationJson is null ? SourceStatus.Unavailable : SourceStatus.UnsafeState, before.Diagnostic);

        var sessions = await Get("rest/sessions/");
        var durations = await Get("rest/sessions/GetSessionsInfoForEvent");
        // Recheck directly before garage access. State changes revoke the whole observation.
        var gate = NavigationPolicy.Evaluate(await Get("navigation/state"), options);
        if (!gate.Safe || gate.State != before.State) return Blocked(SourceStatus.UnsafeState, "Navigation changed before garage access.");
        var setup = await Get("rest/garage/UIScreen/SessionSetup");
        var after = NavigationPolicy.Evaluate(await Get("navigation/state"), options);
        if (!after.Safe || after.State != before.State) return Blocked(SourceStatus.UnsafeState, "Navigation changed during polling; values discarded.");
        var snapshot = LmuJsonParser.Parse(sessions, durations, setup, started);
        return snapshot with { Diagnostics = snapshot.Diagnostics.Concat(diagnostics).ToArray() };
    }
}

public sealed class FixtureLmuSource(string directory) : ILmuSource
{
    public async Task<LmuSnapshot> PollAsync(CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        async Task<string?> Read(string name)
        {
            try
            {
                await using var stream = File.OpenRead(Path.Combine(directory, name));
                return await BoundedJson.ReadAsync(stream, 2 * 1024 * 1024, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { diagnostics.Add($"Fixture {name}: {ex.Message}"); return null; }
        }
        var snapshot = LmuJsonParser.Parse(await Read("sessions.json"), await Read("durations.json"),
            await Read("setup.json"), DateTimeOffset.UtcNow);
        return snapshot with { Diagnostics = snapshot.Diagnostics.Concat(diagnostics).ToArray() };
    }
}
