using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FactorySeatCompanion.Core;

// Dependency-free executable test runner. A failing assertion yields a nonzero exit code.
var tests = new List<(string Name, Func<Task> Run)>();
var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
var now = DateTimeOffset.Parse("2026-10-09T12:00:00Z");
var options = new CompanionOptions
{
    SafeSetupStates = ["SYNTHETIC_SETUP_ONLY"], RequestTimeoutSeconds = 0.1,
    LayoutMappings = [new("BahrainWEC_2023", "BAHRAINWEC_ENDCE", "layoutEndurance"),
        new("BahrainWEC_2023", "BAHRAINWEC_OUTER", "layoutOuter")]
};
string Fixture(string name) => File.ReadAllText(Path.Combine(fixtures, name));
var briefingJson = Fixture("briefing.provisional.json");
var sessions = Fixture("lmu/sessions.json");
var durations = Fixture("lmu/durations.json");
var setup = Fixture("lmu/setup.json");
var navigation = Fixture("lmu/navigation.setup.synthetic.json");
var replay = Fixture("lmu/navigation.replay.synthetic.json");
var briefing = ProvisionalJsonBriefingReader.Parse(briefingJson, now, TimeSpan.FromHours(2));
var live = LmuJsonParser.Parse(sessions, durations, setup, now);
var validator = new SettingsValidator(options);

void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> action) => tests.Add((name, action));
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Status(ValidationReport report, string id, CheckStatus expected) =>
    Assert(report.Checks.Single(c => c.Id == id).Status == expected, $"{id}: expected {expected}");
ValidationReport Report(LmuSnapshot? snapshot = null, ProvisionalBriefing? requirement = null) =>
    validator.Validate(requirement is null ? briefing : briefing with { Briefing = requirement }, snapshot ?? live, now);
string Change(string json, Action<JsonNode> update)
{
    var node = JsonNode.Parse(json)!; update(node); return node.ToJsonString();
}

Test("Complete synthetic setup is ready", () => Assert(Report().ReadyToRace));
Test("Duration comes from RACE lengthTime, never percentage", () =>
{
    Assert(live.RaceMinutes == 360);
    var parsed = LmuJsonParser.Parse(sessions, "{}", setup, now);
    Assert(parsed.RaceMinutes is null && !Report(parsed).ReadyToRace);
});
Test("12/24-hour start-time normalization", () =>
{
    Assert(Normalization.Time("11:15 AM") == "11:15");
    Assert(Normalization.Time("12:00 AM") == "00:00");
    Assert(Normalization.Time("12:00 PM") == "12:00");
    Assert(Normalization.Time("1:05 PM") == "13:05");
    Assert(Normalization.Time("25:15") is null);
});
Test("Numeric labels normalize without interpreting percent/enum", () =>
{
    Assert(Normalization.Multiplier("Realistic") == 1);
    Assert(Normalization.Multiplier("Normal") == 1);
    Assert(Normalization.Multiplier("x 2") == 2);
    Assert(Normalization.Multiplier("2×") == 2);
    Assert(Normalization.Multiplier("Off") == 0);
    Assert(Normalization.Multiplier("100%") is null);
    Assert(Normalization.Number("NaN") is null);
    Assert(Normalization.Number("Infinity") is null);
    Assert(Normalization.Number("-1") is null);
});
Test("currentValue alone remains unknown", () =>
{
    var json = Change(sessions, n => n["SESSSET_Fuel_Usage"]!.AsObject().Remove("stringValue"));
    Status(Report(LmuJsonParser.Parse(json, durations, setup, now)), "fuel", CheckStatus.Unknown);
});
Test("Missing requirements cannot default to matching", () =>
{
    var r = Report(requirement: briefing.Briefing! with { RaceMinutes = null, FuelMultiplier = null, Car = null });
    Assert(!r.ReadyToRace);
    Status(r, "duration", CheckStatus.Unknown); Status(r, "fuel", CheckStatus.Unknown); Status(r, "carModel", CheckStatus.Unknown);
});
Test("Numeric tolerance and invalid values", () =>
{
    Status(Report(live with { FuelMultiplier = 1.0005 }), "fuel", CheckStatus.Match);
    Status(Report(live with { FuelMultiplier = 1.01 }), "fuel", CheckStatus.Mismatch);
    Status(Report(live with { FuelMultiplier = double.NaN }), "fuel", CheckStatus.Unknown);
    Status(Report(live with { TimeScale = 0 }), "timeScale", CheckStatus.Unknown);
    Status(Report(live with { RaceMinutes = 0 }), "duration", CheckStatus.Unknown);
});

var missing = new (string Id, LmuSnapshot Snapshot)[]
{
    ("circuit", live with { TrackFolder = null }), ("layout", live with { SceneDescription = null }),
    ("carModel", live with { CarFolder = null }), ("carClass", live with { Classes = null }),
    ("carNumber", live with { Number = null }), ("team", live with { Team = null }),
    ("duration", live with { RaceMinutes = null }), ("fuel", live with { FuelMultiplier = null }),
    ("tyres", live with { TireMultiplier = null }), ("timeScale", live with { TimeScale = null }),
    ("startTime", live with { StartTime = null })
};
foreach (var item in missing) Test($"Missing live {item.Id} blocks readiness", () =>
{
    var r = Report(item.Snapshot); Assert(!r.ReadyToRace); Status(r, item.Id, CheckStatus.Unknown);
});
var mismatches = new (string Id, LmuSnapshot Snapshot)[]
{
    ("circuit", live with { TrackFolder = "Spa_2023" }),
    ("layout", live with { SceneDescription = "BAHRAINWEC_OUTER" }),
    ("carModel", live with { CarFolder = "BMW_M_Hybrid_V8_2023" }),
    ("carClass", live with { Classes = ["Hyper", "WEC2024"] }),
    ("carNumber", live with { Number = "32" }), ("team", live with { Team = "Other team" }),
    ("duration", live with { RaceMinutes = 100 }), ("fuel", live with { FuelMultiplier = 2 }),
    ("tyres", live with { TireMultiplier = 1 }), ("timeScale", live with { TimeScale = 1 }),
    ("startTime", live with { StartTime = "13:15" })
};
foreach (var item in mismatches) Test($"Mismatch {item.Id} blocks readiness", () =>
{
    var r = Report(item.Snapshot); Assert(!r.ReadyToRace); Status(r, item.Id, CheckStatus.Mismatch);
});

Test("Unknown/duplicate layout mapping is never inferred from name", () =>
{
    Status(Report(live with { SceneDescription = "BAHRAIN_NEW_LAYOUT" }), "layout", CheckStatus.Unknown);
    var v = new SettingsValidator(options with { LayoutMappings = options.LayoutMappings.Concat(options.LayoutMappings).ToArray() });
    Status(v.Validate(briefing, live, now), "layout", CheckStatus.Unknown);
});
Test("Vehicle folder extracted from installed path, not manufacturer", () =>
{
    Assert(live.CarFolder == "BMW_M4_LMGT3_2023");
    Assert(Normalization.VehicleFolder("Installed/Vehicles/BMW_M4_LMGT3_2023/1.0/file.veh") == live.CarFolder);
    Assert(Normalization.VehicleFolder("BMW/file.veh") is null);
    Assert(Normalization.VehicleFolder("Installed/Vehicles/../file.veh") is null);
});
Test("Permitted numbers and exact locked number", () =>
{
    var b = briefing.Briefing! with { Car = briefing.Briefing!.Car! with { Number = null, PermittedNumbers = ["31", "46"] } };
    Status(Report(live with { Number = "#46" }, b), "carNumber", CheckStatus.Match);
    Status(Report(live with { Number = "32" }, b), "carNumber", CheckStatus.Mismatch);
    Status(Report(live with { Number = "46" }), "carNumber", CheckStatus.Mismatch);
});
Test("Any livery explicitly skips number/team", () =>
{
    var b = briefing.Briefing! with { Car = briefing.Briefing!.Car! with
        { Number = null, PermittedNumbers = [], AnyNumber = true, TeamRequired = false } };
    var r = Report(live with { Number = null, Team = null }, b);
    Assert(r.ReadyToRace); Status(r, "carNumber", CheckStatus.NotApplicable); Status(r, "team", CheckStatus.NotApplicable);
});
Test("Contradictory number policy remains unknown", () =>
{
    var b = briefing.Briefing! with { Car = briefing.Briefing!.Car! with { AnyNumber = true } };
    Status(Report(requirement: b), "carNumber", CheckStatus.Unknown);
});
Test("Custom team cannot be proved by matching team name", () =>
{
    var b = briefing.Briefing! with { Car = briefing.Briefing!.Car! with { Number = null, CustomTeam = true } };
    var r = Report(requirement: b); Assert(!r.ReadyToRace); Status(r, "customTeam", CheckStatus.Unknown);
});
Test("Guest drive uses effective round car", () =>
{
    var b = briefing.Briefing! with { Car = briefing.Briefing!.Car! with { Folder = "Ferrari_499P_2023", Class = "Hyper" } };
    Status(Report(requirement: b), "carModel", CheckStatus.Mismatch);
    Status(Report(live with { CarFolder = "Ferrari_499P_2023", Classes = ["Hyper"] }, b), "carModel", CheckStatus.Match);
});
Test("Livery check when explicitly required", () =>
{
    var b = briefing.Briefing! with { Car = briefing.Briefing!.Car! with { LiveryName = "Sponsor livery" } };
    Status(Report(requirement: b), "livery", CheckStatus.Mismatch);
    Status(Report(live with { LiveryName = null }, b), "livery", CheckStatus.Unknown);
});
Test("Optional/default start time must be explicit", () =>
{
    Status(Report(requirement: briefing.Briefing! with { StartTime = null }), "startTime", CheckStatus.Unknown);
    var b = briefing.Briefing! with { StartTime = null, StartTimeRequired = false };
    Status(Report(requirement: b), "startTime", CheckStatus.NotApplicable);
    Status(Report(requirement: briefing.Briefing! with { StartTimeRequired = false }), "startTime", CheckStatus.Unknown);
});
Test("Stale/invalid briefing clears all matches", () =>
{
    var stale = ProvisionalJsonBriefingReader.Parse(briefingJson, now.AddHours(3), TimeSpan.FromHours(2));
    Assert(stale.Status == SourceStatus.Stale);
    Assert(validator.Validate(stale, live, now).Checks.All(c => c.Status == CheckStatus.Unknown));
    foreach (var json in new[] { "{}", "{", Change(briefingJson, n => n["schemaVersion"] = 2),
        Change(briefingJson, n => n["state"] = "Completed"), Change(briefingJson, n => n["state"] = "Skipped"),
        Change(briefingJson, n => n["generatedAt"] = now.AddHours(1).ToString("O")),
        Change(briefingJson, n => n.AsObject().Remove("generatedAt")) })
        Assert(ProvisionalJsonBriefingReader.Parse(json, now, TimeSpan.FromHours(2)).Status == SourceStatus.Invalid);
});
Test("Missing header metadata blocks readiness", () => Assert(!Report(requirement: briefing.Briefing! with { RoundNumber = null }).ReadyToRace));
Test("Stale observation clears matches", () => Assert(!Report(live with { ObservedAt = now.AddMinutes(-1) }).ReadyToRace));
Test("Endpoint failure clears only affected observations", () =>
{
    var r = Report(LmuJsonParser.Parse("{", durations, setup, now));
    Status(r, "fuel", CheckStatus.Unknown); Status(r, "duration", CheckStatus.Match); Status(r, "carModel", CheckStatus.Match);
    Assert(!r.ReadyToRace);
});
Test("Duplicate RACE sessions are ambiguous", () =>
{
    var json = "{\"scheduledSessions\":[{\"name\":\"RACE\",\"lengthTime\":360},{\"name\":\"RACE\",\"lengthTime\":360}]}";
    Assert(LmuJsonParser.Parse(sessions, json, setup, now).RaceMinutes is null);
});
Test("Duplicate JSON properties are rejected", () =>
{
    Assert(LmuJsonParser.Parse(sessions, durations, "{\"selectedCar\":{},\"selectedCar\":{}}", now).CarFolder is null);
    Assert(ProvisionalJsonBriefingReader.Parse(briefingJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"), now,
        TimeSpan.FromHours(2)).Status == SourceStatus.Invalid);
});
Test("Wrapped setting records accepted only when unique", () =>
{
    var wrapped = "{\"settings\":[{\"name\":\"SESSSET_Fuel_Usage\",\"stringValue\":\"Realistic\"}]}";
    Assert(LmuJsonParser.Parse(wrapped, durations, setup, now).FuelMultiplier == 1);
    Assert(LmuJsonParser.Parse("{\"a\":" + sessions + ",\"b\":" + sessions + "}", durations, setup, now).FuelMultiplier is null);
});
Test("Unknown navigation denied by default", () =>
{
    Assert(!NavigationPolicy.Evaluate(navigation, new CompanionOptions()).Safe);
    Assert(!NavigationPolicy.Evaluate("{}", options).Safe);
    Assert(!NavigationPolicy.Evaluate("{", options).Safe);
    Assert(!NavigationPolicy.Evaluate(null, options).Safe);
    Assert(NavigationPolicy.Evaluate(navigation, options).Safe);
});
Test("Replay/loading incl string-encoded loadingData denied", () =>
{
    Assert(!NavigationPolicy.Evaluate(replay, options).Safe);
    Assert(!NavigationPolicy.Evaluate(Change(navigation, n => n["isLoading"] = true), options).Safe);
    Assert(!NavigationPolicy.Evaluate(Change(navigation, n => n["loadingStatus"]!["loadingData"] = "not JSON"), options).Safe);
});
Test("Embedded explicit false replay flags do not trigger a replay", () =>
{
    var json = Change(navigation, n => n["loadingStatus"]!["loadingData"] = "{\"isReplay\":false,\"isLoading\":false}");
    Assert(NavigationPolicy.Evaluate(json, options).Safe);
});
Test("Unrecognized readable labels are diagnosed, not guessed", () =>
{
    var json = Change(sessions, n => n["SESSSET_Fuel_Usage"]!["stringValue"] = "Enum value 3");
    var parsed = LmuJsonParser.Parse(json, durations, setup, now);
    Assert(parsed.FuelMultiplier is null);
    Assert(parsed.Diagnostics.Any(d => d.Contains("unsupported readable value")));
});
Test("JSON-pointer nested state supported", () =>
    Assert(NavigationPolicy.Evaluate("{\"navigation\":{\"route\":\"SYNTHETIC_SETUP_ONLY\"}}", options with { NavigationStatePointer = "/navigation/route" }).Safe));
Test("Polling configuration rejects unsafe bounds", () =>
{
    foreach (var o in new[] { options with { PollSeconds = 0 }, options with { RequestTimeoutSeconds = 0 },
        options with { BriefingMaxAgeMinutes = double.NaN }, options with { SafeSetupStates = ["Replay"] } })
    {
        try { o.Validate(); throw new Exception("Should reject config"); } catch (InvalidDataException) { }
    }
});

AsyncTest("File refresh, partial write, deletion, and recovery", async () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "FactorySeatTests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "briefing.json");
    try
    {
        var reader = new ProvisionalJsonBriefingReader(path, TimeSpan.FromHours(2));
        Assert((await reader.ReadAsync(now)).Status == SourceStatus.Unavailable);
        await File.WriteAllTextAsync(path, briefingJson);
        Assert((await reader.ReadAsync(now)).Briefing!.RoundNumber == 3);
        await File.WriteAllTextAsync(path, Change(briefingJson, n => n["roundNumber"] = 4));
        Assert((await reader.ReadAsync(now)).Briefing!.RoundNumber == 4);
        await File.WriteAllTextAsync(path, "{");
        Assert((await reader.ReadAsync(now)).Briefing is null);
        await File.WriteAllTextAsync(path, briefingJson);
        Assert((await reader.ReadAsync(now)).Status == SourceStatus.Available);
        File.Delete(path);
        Assert((await reader.ReadAsync(now)).Briefing is null);
    }
    finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
});

HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
HttpResponseMessage Endpoint(HttpRequestMessage request, string nav) => Response(request.RequestUri!.AbsolutePath switch
{
    "/navigation/state" => nav,
    "/rest/sessions/" => sessions,
    "/rest/sessions/GetSessionsInfoForEvent" => durations,
    "/rest/garage/UIScreen/SessionSetup" => setup,
    _ => throw new Exception("Unexpected endpoint")
});
AsyncTest("Live transport issues only loopback GETs", async () =>
{
    using var handler = new FakeHandler((request, _) => Task.FromResult(Endpoint(request, navigation)));
    using var http = new HttpClient(handler);
    var parsed = await new LmuApiClient(http, options).PollAsync();
    Assert(parsed.CarFolder == live.CarFolder && parsed.RaceMinutes == 360);
    Assert(handler.Requests.Count == 6);
    Assert(handler.Requests.All(r => r.Method == HttpMethod.Get && r.Uri.Host == "127.0.0.1" && r.Uri.Port == 6397));
});
AsyncTest("Replay never requests garage/settings", async () =>
{
    using var handler = new FakeHandler((r, _) => Task.FromResult(Endpoint(r, replay)));
    using var http = new HttpClient(handler);
    var parsed = await new LmuApiClient(http, options).PollAsync();
    Assert(parsed.Status == SourceStatus.UnsafeState && handler.Requests.Count == 1);
});
AsyncTest("Navigation inspection works without allowlist and makes only one GET", async () =>
{
    using var handler = new FakeHandler((r, _) => Task.FromResult(Endpoint(r, replay)));
    using var http = new HttpClient(handler);
    Assert(await new LmuApiClient(http, new CompanionOptions()).InspectNavigationAsync() == replay);
    Assert(handler.Requests.Count == 1 && handler.Requests[0].Uri.AbsolutePath == "/navigation/state");
});
AsyncTest("State transition before garage suppresses endpoint", async () =>
{
    int navReads = 0;
    using var handler = new FakeHandler((r, _) =>
    {
        var nav = r.RequestUri!.AbsolutePath == "/navigation/state" && ++navReads > 1 ? replay : navigation;
        return Task.FromResult(Endpoint(r, nav));
    });
    using var http = new HttpClient(handler);
    var parsed = await new LmuApiClient(http, options).PollAsync();
    Assert(parsed.Status == SourceStatus.UnsafeState);
    Assert(handler.Requests.All(r => !r.Uri.AbsolutePath.Contains("garage")));
});
AsyncTest("State transition after garage discards complete setup", async () =>
{
    int navReads = 0;
    using var handler = new FakeHandler((r, _) => Task.FromResult(Endpoint(r,
        r.RequestUri!.AbsolutePath == "/navigation/state" && ++navReads > 2 ? replay : navigation)));
    using var http = new HttpClient(handler);
    var parsed = await new LmuApiClient(http, options).PollAsync();
    Assert(parsed.Status == SourceStatus.UnsafeState && parsed.CarFolder is null);
});
AsyncTest("Timeout and connection recovery do not reuse values", async () =>
{
    bool offline = true;
    using var handler = new FakeHandler(async (r, token) =>
    {
        if (offline) await Task.Delay(Timeout.Infinite, token);
        return Endpoint(r, navigation);
    });
    using var http = new HttpClient(handler);
    var client = new LmuApiClient(http, options);
    Assert((await client.PollAsync()).Status == SourceStatus.Unavailable);
    offline = false;
    Assert((await client.PollAsync()).CarFolder == live.CarFolder);
    offline = true;
    Assert((await client.PollAsync()).CarFolder is null);
});
AsyncTest("Failed endpoint still allows independent checks", async () =>
{
    using var handler = new FakeHandler((r, _) => Task.FromResult(r.RequestUri!.AbsolutePath == "/rest/sessions/"
        ? new HttpResponseMessage(HttpStatusCode.NotFound) : Endpoint(r, navigation)));
    using var http = new HttpClient(handler);
    var parsed = await new LmuApiClient(http, options).PollAsync();
    Assert(parsed.FuelMultiplier is null && parsed.RaceMinutes == 360 && parsed.CarFolder == live.CarFolder);
});
AsyncTest("Oversized HTTP response becomes unavailable evidence", async () =>
{
    using var handler = new FakeHandler((r, _) => Task.FromResult(r.RequestUri!.AbsolutePath == "/rest/sessions/"
        ? Response(new string('x', 2 * 1024 * 1024 + 1)) : Endpoint(r, navigation)));
    using var http = new HttpClient(handler);
    var parsed = await new LmuApiClient(http, options).PollAsync();
    Assert(parsed.FuelMultiplier is null && parsed.CarFolder == live.CarFolder);
    Assert(parsed.Diagnostics.Any(d => d.Contains("size limit")));
});
AsyncTest("Cancellation propagates to caller", async () =>
{
    using var handler = new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("{}"); });
    using var http = new HttpClient(handler);
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { await new LmuApiClient(http, options).PollAsync(cts.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { }
});

int failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri Uri)> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!));
        return respond(request, cancellationToken);
    }
}
