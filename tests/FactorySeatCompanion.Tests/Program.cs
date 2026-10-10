using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FactorySeatCompanion.Core;
using FactorySeatCompanion.SimHub;

// Dependency-free executable test runner. A failing assertion yields a nonzero exit code.
var tests = new List<(string Name, Func<Task> Run)>();
var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
var now = DateTimeOffset.Parse("2026-10-09T12:00:00Z");
var options = new CompanionOptions
{
    SafeSetupStates = ["SYNTHETIC_SETUP_ONLY"], RequestTimeoutSeconds = 2,
    LayoutMappings = [new("BahrainWEC_2023", "BAHRAINWEC_ENDCE", "layoutEndurance"),
        new("BahrainWEC_2023", "BAHRAINWEC_OUTER", "layoutOuter")]
};
string Fixture(string name) => File.ReadAllText(Path.Combine(fixtures, name));
var briefingJson = Fixture("briefing.json");
var sessions = Fixture("lmu/sessions.json");
var durations = Fixture("lmu/durations.json");
var setup = Fixture("lmu/setup.json");
var navigation = Fixture("lmu/navigation.setup.synthetic.json");
var replay = Fixture("lmu/navigation.replay.synthetic.json");
Func<bool> running = () => true;
var briefing = FactorySeatBriefingReader.Parse(briefingJson, running);
var live = LmuJsonParser.Parse(sessions, durations, setup, now);
var validator = new SettingsValidator(options);

void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> action) => tests.Add((name, action));
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Status(ValidationReport report, string id, CheckStatus expected) =>
    Assert(report.Checks.Single(c => c.Id == id).Status == expected, $"{id}: expected {expected}");
ValidationReport Report(LmuSnapshot? snapshot = null, RoundRequirement? requirement = null) =>
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
Test("Duration is unknown unless the finish criteria is Time", () =>
{
    // Live LMU: "% Track Time" at 10% showed 2h24 in the menu while RACE lengthTime was 48.
    var percent = Change(sessions, n => n["SESSSET_Finish_Criteria"]!["stringValue"] = "% Track Time");
    var race = "{\"scheduledSessions\":[{\"name\":\"RACE\",\"lengthTime\":48}]}";
    var parsed = LmuJsonParser.Parse(percent, race, setup, now);
    Assert(parsed.RaceMinutes is null && parsed.Diagnostics.Any(d => d.Contains("SESSSET_Finish_Criteria")));
    Status(Report(parsed), "duration", CheckStatus.Unknown);
    var missing = Change(sessions, n => n.AsObject().Remove("SESSSET_Finish_Criteria"));
    Assert(LmuJsonParser.Parse(missing, durations, setup, now).RaceMinutes is null);
    var timed = Change(sessions, n => n["SESSSET_Finish_Criteria"]!["stringValue"] = " time ");
    Assert(LmuJsonParser.Parse(timed, durations, setup, now).RaceMinutes == 360);
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
Test("Locked number compares exactly", () =>
{
    Status(Report(live with { Number = "#31" }), "carNumber", CheckStatus.Match);
    Status(Report(live with { Number = "46" }), "carNumber", CheckStatus.Mismatch);
});
Test("Null number and team mean any livery", () =>
{
    var b = briefing.Briefing! with { Car = briefing.Briefing!.Car! with { Number = null, AnyNumber = true, Team = null } };
    var r = Report(live with { Number = null, Team = null }, b);
    Assert(r.ReadyToRace); Status(r, "carNumber", CheckStatus.NotApplicable); Status(r, "team", CheckStatus.NotApplicable);
});
Test("Missing number field remains unknown", () =>
{
    var b = briefing.Briefing! with { Car = briefing.Briefing!.Car! with { Number = null, AnyNumber = null } };
    Status(Report(requirement: b), "carNumber", CheckStatus.Unknown);
    var json = Change(briefingJson, n => n["round"]!["car"]!.AsObject().Remove("number"));
    Assert(FactorySeatBriefingReader.Parse(json, running).Briefing!.Car!.AnyNumber is null);
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
Test("Null start time means LMU default; missing stays unknown", () =>
{
    Status(Report(requirement: briefing.Briefing! with { StartTime = null, StartTimeDefault = null }), "startTime", CheckStatus.Unknown);
    var json = Change(briefingJson, n => n["round"]!["settings"]!["startTime"] = null);
    var parsed = FactorySeatBriefingReader.Parse(json, running);
    Assert(parsed.Briefing!.StartTimeDefault == true);
    Status(validator.Validate(parsed, live, now), "startTime", CheckStatus.NotApplicable);
});
Test("Factory Seat example briefing parses", () =>
{
    var parsed = FactorySeatBriefingReader.Parse(Fixture("briefing.spec-example.json"), running);
    var r = parsed.Briefing!;
    Assert(parsed.Status == SourceStatus.Available, parsed.Diagnostic);
    Assert(r.CareerName == "Road to Hypercar" && r.EventName == "Qatar 1812 km" && r.RoundNumber == 1 && r.RoundCount == 8);
    Assert(r.TrackFolder == "Qatar_2024" && r.LayoutFile == "layoutQatar" && r.Guest == false);
    Assert(r.RaceMinutes == 30 && r.FuelMultiplier == 3 && r.TireMultiplier == 3 && r.TimeScale == 20);
    Assert(r.StartTime == "11:00" && r.StartTimeDefault == false);
    Assert(r.Car!.Folder == "Ford_Mustang_GT3_2024" && r.Car.Class == "GT3");
    Assert(r.Car.AnyNumber == true && r.Car.Number is null && r.Car.Team is null && r.Car.CustomTeam == false);
});
Test("Unknown fields are ignored", () =>
{
    var json = Change(briefingJson, n => { n["futureField"] = 1; n["round"]!["car"]!["futureCarField"] = "x"; });
    Assert(FactorySeatBriefingReader.Parse(json, running).Status == SourceStatus.Available);
});
Test("Closed career is shown but never verified", () =>
{
    bool probed = false;
    var closed = FactorySeatBriefingReader.Parse(Change(briefingJson, n => n["open"] = false), () => probed = true);
    Assert(closed.Status == SourceStatus.Stale && closed.Briefing!.RoundNumber == 3 && !probed);
    Assert(validator.Validate(closed, live, now).Checks.All(c => c.Status == CheckStatus.Unknown));
});
Test("Open flag left by a crash is stale when Factory Seat is not running", () =>
{
    var crashed = FactorySeatBriefingReader.Parse(briefingJson, () => false);
    Assert(crashed.Status == SourceStatus.Stale && crashed.Diagnostic.Contains("not running"));
    Assert(!validator.Validate(crashed, live, now).ReadyToRace);
});
Test("No round between seasons", () =>
{
    var parsed = FactorySeatBriefingReader.Parse(Change(briefingJson, n => n["round"] = null), running);
    Assert(parsed.Status == SourceStatus.Unavailable && parsed.Briefing is null);
});
Test("Invalid briefings are rejected", () =>
{
    foreach (var json in new[] { "{}", "{", Change(briefingJson, n => n["version"] = 2),
        Change(briefingJson, n => n["format"] = "factory-seat-companion-provisional"),
        Change(briefingJson, n => n.AsObject().Remove("version")), Change(briefingJson, n => n.AsObject().Remove("open")),
        Change(briefingJson, n => n["open"] = "true"),
        Change(briefingJson, n => n["round"]!["state"] = "Completed"), Change(briefingJson, n => n["round"]!["state"] = "Skipped") })
        Assert(FactorySeatBriefingReader.Parse(json, running).Status == SourceStatus.Invalid, json);
    Assert(FactorySeatBriefingReader.Parse(Change(briefingJson, n => n["version"] = 2), running).Diagnostic.Contains("update the companion"));
});
Test("Upcoming round is accepted", () =>
    Assert(FactorySeatBriefingReader.Parse(Change(briefingJson, n => n["round"]!["state"] = "Upcoming"), running).Status == SourceStatus.Available));
Test("Unknown car folder blocks readiness", () =>
{
    var parsed = FactorySeatBriefingReader.Parse(Change(briefingJson, n => n["round"]!["car"]!["folder"] = null), running);
    var r = validator.Validate(parsed, live, now);
    Assert(!r.ReadyToRace); Status(r, "carModel", CheckStatus.Unknown);
});
Test("Empty team is not compared", () =>
{
    var parsed = FactorySeatBriefingReader.Parse(Change(briefingJson, n => n["round"]!["car"]!["team"] = ""), running);
    Status(validator.Validate(parsed, live with { Team = "Other team" }, now), "team", CheckStatus.NotApplicable);
});
Test("Missing header metadata blocks readiness", () => Assert(!Report(requirement: briefing.Briefing! with { RoundNumber = null }).ReadyToRace));
Test("Stale observation clears matches", () => Assert(!Report(live with { ObservedAt = now.AddMinutes(-1) }).ReadyToRace));
Test("Endpoint failure clears only affected observations", () =>
{
    var r = Report(LmuJsonParser.Parse("{", durations, setup, now));
    // Duration needs SESSSET_Finish_Criteria from the sessions endpoint, so it is affected too.
    Status(r, "fuel", CheckStatus.Unknown); Status(r, "duration", CheckStatus.Unknown); Status(r, "carModel", CheckStatus.Match);
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
    Assert(FactorySeatBriefingReader.Parse(briefingJson.Replace("\"version\": 1", "\"version\": 1, \"version\": 1"),
        running).Status == SourceStatus.Invalid);
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
Test("Live navigation shape: object state and loadingStatus.loading", () =>
{
    var liveNav = Fixture("lmu/navigation.live-shape.synthetic.json");
    var gameState = options with { NavigationStatePointer = "/state/gameState", SafeSetupStates = ["GSTATE_SETUP"] };
    var objectPointer = NavigationPolicy.Evaluate(liveNav, options with { NavigationStatePointer = "/state", SafeSetupStates = ["GSTATE_SETUP"] });
    Assert(!objectPointer.Safe && objectPointer.Diagnostic.Contains("single value"));
    Assert(!NavigationPolicy.Evaluate(liveNav, new CompanionOptions { NavigationStatePointer = "/state/gameState" }).Safe);
    Assert(NavigationPolicy.Evaluate(liveNav, gameState).Safe);
    Assert(!NavigationPolicy.Evaluate(Change(liveNav, n => n["loadingStatus"]!["loading"] = true), gameState).Safe);
    Assert(!NavigationPolicy.Evaluate(Change(liveNav, n => n["loadingStatus"]!["loading"] = 1), gameState).Safe);
    Assert(!NavigationPolicy.Evaluate(Change(liveNav, n => n["state"]!["gameState"] = "GSTATE_DYN"), gameState).Safe);
});
Test("Screen comes from live navigation values", () =>
{
    // Values captured on 2026-10-10; the main menu and race setup are both GSTATE_SETUP / NAV_MAIN_MENU.
    var liveNav = Fixture("lmu/navigation.live-shape.synthetic.json");
    string Nav(string game, string navigationState, bool loading = false) => Change(liveNav, n =>
    {
        n["state"]!["gameState"] = game; n["state"]!["navigationState"] = navigationState; n["loadingStatus"]!["loading"] = loading;
    });
    Assert(LmuScreenClassifier.Classify(liveNav) == LmuScreen.Menu);
    Assert(LmuScreenClassifier.Classify(Nav("GSTATE_SETUP", "NAV_MAIN_MENU", loading: true)) == LmuScreen.Loading);
    Assert(LmuScreenClassifier.Classify(Nav("GSTATE_INIT", "NAV_EVENT")) == LmuScreen.Loading);
    Assert(LmuScreenClassifier.Classify(Nav("GSTATE_RESTART", "NAV_EVENT")) == LmuScreen.Loading);
    Assert(LmuScreenClassifier.Classify(Nav("GSTATE_DYN", "NAV_EVENT")) == LmuScreen.InSession);
    Assert(LmuScreenClassifier.Classify(Nav("GSTATE_DYN", "NAV_REALTIME")) == LmuScreen.InSession);
    Assert(LmuScreenClassifier.Classify(Nav("GSTATE_POST", "NAV_MAIN_MENU")) == LmuScreen.InSession);
    Assert(LmuScreenClassifier.Classify(Nav("GSTATE_SOMETHING", "NAV_ELSEWHERE")) == LmuScreen.Unknown);
    Assert(LmuScreenClassifier.Classify(null) == LmuScreen.Unknown);
    Assert(LmuScreenClassifier.Classify("not JSON") == LmuScreen.Unknown);
    Assert(LmuScreenClassifier.Classify(navigation) == LmuScreen.Unknown);
});
Test("JSON-pointer nested state supported", () =>
    Assert(NavigationPolicy.Evaluate("{\"navigation\":{\"route\":\"SYNTHETIC_SETUP_ONLY\"}}", options with { NavigationStatePointer = "/navigation/route" }).Safe));
Test("Polling configuration rejects unsafe bounds", () =>
{
    foreach (var o in new[] { options with { PollSeconds = 0 }, options with { RequestTimeoutSeconds = 0 },
        options with { SnapshotMaxAgeSeconds = double.NaN }, options with { SafeSetupStates = ["Replay"] } })
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
        var reader = new FactorySeatBriefingReader(path, running);
        Assert((await reader.ReadAsync(now)).Status == SourceStatus.Unavailable);
        await File.WriteAllTextAsync(path, briefingJson);
        Assert((await reader.ReadAsync(now)).Briefing!.RoundNumber == 3);
        await File.WriteAllTextAsync(path, Change(briefingJson, n => n["round"]!["number"] = 4));
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
    Assert(parsed.CarFolder == live.CarFolder && parsed.RaceMinutes == 360, string.Join("; ", parsed.Diagnostics));
    Assert(handler.Requests.Count == 6, $"Expected 6 requests, got {handler.Requests.Count}.");
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
    var client = new LmuApiClient(http, options with { RequestTimeoutSeconds = 0.1 });
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
    Assert(parsed.FuelMultiplier is null && parsed.RaceMinutes is null && parsed.CarFolder == live.CarFolder);
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
// SimHub plugin bridge: the CLI's --jsonl line feeds DashboardState.
var jsonl = new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = false };
CompanionMessage Bridge(ValidationReport report) => JsonSerializer.Deserialize<CompanionMessage>(
    JsonSerializer.Serialize(new { mode = "Fixture", report }, jsonl), JsonDefaults.Options)!;
DashboardState Dash(ValidationReport report, double ageSeconds = 0) =>
    DashboardState.Create(Bridge(report), report.ObservedAt.AddSeconds(ageSeconds), TimeSpan.FromSeconds(10), "Running");
Test("Bridge line is single-line and round-trips rows", () =>
{
    var line = JsonSerializer.Serialize(new { mode = "Fixture", report = Report() }, jsonl);
    Assert(!line.Contains('\n'), "JSONL output must be one line");
    var state = Dash(Report());
    Assert(state.ReadyToRace && state.OverallStatus == "READY TO RACE" && state.Mode == "Fixture");
    Assert(state.Rows.Count == DashboardState.RowSlots);
    Assert(state.Rows[0].Visible && state.Rows[0].Id == "circuit" && state.Rows[0].Status == "Match");
    Assert(state.Rows.Count(r => r.Visible) == Report().Checks.Count);
    Assert(state.Headline.Contains("Round"), state.Headline);
});
Test("All optional rows fit the dashboard slots", () =>
{
    var report = Report(requirement: briefing.Briefing! with { Car = briefing.Briefing!.Car! with { CustomTeam = true } });
    Assert(report.Checks.Any(c => c.Id == "customTeam") && report.Checks.Count <= DashboardState.RowSlots, $"{report.Checks.Count} rows");
    Assert(Dash(report).Rows.Count(r => r.Visible) == report.Checks.Count);
});
Test("Dashboard mismatch is shown, not ready", () =>
{
    var state = Dash(Report(live with { FuelMultiplier = 2 }));
    Assert(!state.ReadyToRace && state.OverallStatus == "SETUP MISMATCH");
    Assert(state.Rows.Single(r => r.Id == "fuel").Status == "Mismatch");
});
Test("Mismatched rows carry a one-line fix hint in LMU's wording", () =>
{
    var report = Report(live with { FuelMultiplier = 2, TireMultiplier = 1, TimeScale = 1, StartTime = "09:00", Number = "46",
        TrackFolder = "Qatar_2024", CarFolder = "Ford_Mustang_GT3_2024" });
    string Hint(string id) => report.Checks.Single(c => c.Id == id).Hint ?? "(none)";
    Assert(Hint("fuel") == "Set Fuel Usage to Realistic", Hint("fuel"));
    Assert(Hint("tyres") == "Set Tyre Wear to X2", Hint("tyres"));
    Assert(Hint("timeScale") == "Set Time Scale to X4", Hint("timeScale"));
    Assert(Hint("startTime") == "Set Start Time to 11:15 AM", Hint("startTime"));
    Assert(Hint("carNumber") == "Select car #31", Hint("carNumber"));
    Assert(Hint("circuit") == "Select Bahrain International Circuit", Hint("circuit"));
    Assert(Hint("layout") == "Select the Endurance Circuit layout", Hint("layout"));
    Assert(Hint("carModel") == "Select the BMW M4 LMGT3", Hint("carModel"));
    Assert(report.Checks.Where(c => c.Status != CheckStatus.Mismatch).All(c => c.Hint is null));
    Assert(Report().Checks.All(c => c.Hint is null));
    var normal = Report(requirement: briefing.Briefing! with { TimeScale = 1, TireMultiplier = 3 });
    Assert(normal.Checks.Single(c => c.Id == "timeScale").Hint == "Set Time Scale to Normal");
    Assert(normal.Checks.Single(c => c.Id == "tyres").Hint == "Set Tyre Wear to X3");
    // Not current sources give Unknown rows, never hints.
    Assert(Report(live with { FuelMultiplier = 2, ObservedAt = now.AddMinutes(-1) }).Checks.All(c => c.Hint is null));
});
Test("Rows show briefing names, not identifiers", () =>
{
    var report = Report();
    SettingCheck Row(ValidationReport r, string id) => r.Checks.Single(c => c.Id == id);
    Assert(Row(report, "circuit").Required == "Bahrain International Circuit" && Row(report, "circuit").Current == "Bahrain International Circuit");
    Assert(Row(report, "layout").Required == "Endurance Circuit" && Row(report, "carModel").Current == "BMW M4 LMGT3");
    Assert(Row(report, "carClass").Current == "GT3", Row(report, "carClass").Current);
    // A mismatch keeps LMU's own identifier so the driver sees what is actually selected.
    var wrongCar = Report(live with { CarFolder = "Ford_Mustang_GT3_2024" });
    Assert(Row(wrongCar, "carModel").Current == "Ford_Mustang_GT3_2024");
    var noNames = Report(requirement: briefing.Briefing! with { TrackName = null });
    Assert(Row(noNames, "circuit").Required == "BahrainWEC_2023");
    // Unmapped layouts still give the identifiers needed to add a mapping.
    var unmapped = Report(live with { SceneDescription = "BAHRAINWEC_PADDOCK" });
    Assert(unmapped.Diagnostics.Any(d => d.Contains("trackFolder BahrainWEC_2023") && d.Contains("BAHRAINWEC_PADDOCK")));
    Assert(!Report().Diagnostics.Any(d => d.Contains("No layout mapping")));
});
Test("Dashboard shows NotApplicable as Any and lists fix hints", () =>
{
    var any = Dash(Report(requirement: briefing.Briefing! with { Car = briefing.Briefing!.Car! with { AnyNumber = true, Team = null } }));
    var number = any.Rows.Single(r => r.Id == "carNumber");
    Assert(any.ReadyToRace && number.Status == "NotApplicable" && number.StatusText == "Any", number.StatusText);
    Assert(any.Rows.Single(r => r.Id == "fuel").StatusText == "Match" && any.Hints == "");
    Assert(number.Icon == "\u2013" && any.Rows.Single(r => r.Id == "fuel").Icon == "\u2713" && DashboardRow.Hidden.Icon == "");
    Assert(SettingCheck.DisplayStatus(CheckStatus.NotApplicable) == "Any");
    var mismatch = Dash(Report(live with { FuelMultiplier = 2, TimeScale = 1 }));
    Assert(mismatch.Rows.Single(r => r.Id == "fuel").Hint == "Set Fuel Usage to Realistic");
    Assert(mismatch.Rows.Single(r => r.Id == "fuel").Icon == "\u2717");
    Assert(mismatch.Hints == "Set Fuel Usage to Realistic \u00B7 Set Time Scale to X4", mismatch.Hints);
    var stale = Dash(Report(live with { FuelMultiplier = 2 }), 11);
    // Stale or unreadable LMU still lists every required setting, each with an open circle.
    Assert(stale.Hints == "" && stale.Rows.All(r => r.Hint == "") &&
        stale.Rows.Where(r => r.Visible).All(r => r.Icon == "\u25CB" && r.Required != ""));
    Assert(stale.ManualCheck == DashboardState.ManualCheckText && mismatch.ManualCheck == "" && any.ManualCheck == "");
    var offline = Dash(Report(live with { Status = SourceStatus.Unavailable }));
    Assert(offline.ManualCheck.Contains("by hand") && offline.Rows.Where(r => r.Visible).All(r => r.Icon == "\u25CB"));
    var noBriefing = Dash(Report() with { BriefingStatus = SourceStatus.Stale, LmuStatus = SourceStatus.Unavailable });
    Assert(noBriefing.ManualCheck == "");
});
Test("Stale or future report is never ready", () =>
{
    var stale = Dash(Report(), 11);
    Assert(!stale.ReadyToRace && stale.OverallStatus == "NOT VERIFIED" && stale.LmuStatus == "Stale");
    Assert(stale.Rows.Where(r => r.Visible).All(r => r.Status == "Unknown" && r.Current == "Unknown"));
    Assert(stale.Diagnostics.Contains("observed 11s ago"), stale.Diagnostics);
    Assert(!Dash(Report(), -5).ReadyToRace);
    Assert(!Dash(Report(live with { FuelMultiplier = 2 }), 11).OverallStatus.Contains("MISMATCH"));
});
Test("Missing report waits with hidden rows", () =>
{
    var state = DashboardState.Create(null, now, TimeSpan.FromSeconds(10), "Companion CLI not found: x");
    Assert(!state.ReadyToRace && state.Rows.All(r => !r.Visible) && state.BridgeStatus.Contains("not found"));
});
Test("Dashboard does not trust a ready flag over row statuses", () =>
{
    var message = Bridge(Report());
    message.Report!.Checks[2].Status = "Unknown";
    Assert(!DashboardState.Create(message, message.Report.ObservedAt, TimeSpan.FromSeconds(10), "Running").ReadyToRace);
    message = Bridge(Report());
    for (int i = 0; i < 3; i++) message.Report!.Checks.Add(message.Report.Checks[0]);
    Assert(!DashboardState.Create(message, message.Report!.ObservedAt, TimeSpan.FromSeconds(10), "Running").ReadyToRace);
});
Test("Bridge carries the screen; only a fresh report vouches for it", () =>
{
    var line = JsonSerializer.Serialize(new { mode = "Live", screen = LmuScreen.InSession, report = Report() }, jsonl);
    var message = JsonSerializer.Deserialize<CompanionMessage>(line, JsonDefaults.Options)!;
    var observed = message.Report!.ObservedAt;
    Assert(OverlayVisibility.ScreenOf(message, observed, TimeSpan.FromSeconds(10)) == "InSession");
    Assert(OverlayVisibility.ScreenOf(message, observed.AddSeconds(11), TimeSpan.FromSeconds(10)) == "Unknown");
    Assert(OverlayVisibility.ScreenOf(Bridge(Report()), observed, TimeSpan.FromSeconds(10)) == "Unknown");
    Assert(OverlayVisibility.ScreenOf(null, observed, TimeSpan.FromSeconds(10)) == "Unknown");
});
Test("Overlay shows in menus, hides in sessions, and a dismissal lasts until the next session", () =>
{
    var overlay = new OverlayVisibility();
    Assert(overlay.Visible, "unknown screen shows the checklist");
    overlay.Observe("Menu");
    Assert(overlay.Visible);
    overlay.Toggle();
    Assert(!overlay.Visible, "dismissed on the menu");
    overlay.Observe("Unknown");
    overlay.Observe("Menu");
    Assert(!overlay.Visible, "a brief unknown read does not undo a dismissal");
    overlay.Observe("Loading");
    Assert(!overlay.Visible);
    overlay.Observe("InSession");
    Assert(!overlay.Visible);
    overlay.Toggle();
    Assert(overlay.Visible, "the action can show it in a session");
    overlay.Observe("Menu");
    Assert(overlay.Visible && overlay.Screen == "Menu", "back in the menus it shows again");
});
Test("Plugin command line quoting", () =>
{
    Assert(WindowsCommandLine.Quote("plain") == "plain");
    Assert(WindowsCommandLine.Quote("") == "\"\"");
    Assert(WindowsCommandLine.Quote(@"C:\Program Files\SimHub\") == @"""C:\Program Files\SimHub\\""");
    Assert(WindowsCommandLine.Quote("a\"b") == @"""a\""b""");
    Assert(WindowsCommandLine.Join(["--briefing", @"C:\My Docs\b.json"]) == @"--briefing ""C:\My Docs\b.json""");
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
