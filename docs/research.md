# Source and integration investigation

Investigated 2026-10-09. The initial workspace contained no source files or Git repository. A read-only research checkout was fetched under ignored `research/factory-seat`; it is not compiled, referenced, or distributed as part of the companion. No external installed files were changed.

## Factory Seat

Inspected public revision `d0d4b81aa831f989e981b0d219856577438f6c15`:

- [Career.cs](https://github.com/factoryseatlmuapp/factory-seat/blob/d0d4b81aa831f989e981b0d219856577438f6c15/src/LmuCareer.Core/Careers/Career.cs): Round fields include TrackFolder, LayoutFile, StartTime, RaceMinutes, TimeScale, FuelMultiplier, TireMultiplier, Stints, State, GuestCar and GuestNumbers. `Season.NextRound` selects the armed round before an upcoming one. States are Upcoming, Armed, Completed, Skipped. CareerCar has CarType/Class/Number/TeamName, alternate result car types and CustomTeam.
- [career.js briefing and car rows](https://github.com/factoryseatlmuapp/factory-seat/blob/d0d4b81aa831f989e981b0d219856577438f6c15/src/LmuCareer.App/wwwroot/js/views/career.js): custom-team number is unrestricted; otherwise an existing car number is locked; before the first race contract numbers are offered, or any livery if none are known. Guest rows use GuestCar/GuestNumbers. Empty start time is displayed as Default. Time scale 1 is Normal, and fuel/tyre factor 1 is Realistic.
- [ContentCatalog.cs](https://github.com/factoryseatlmuapp/factory-seat/blob/d0d4b81aa831f989e981b0d219856577438f6c15/src/LmuCareer.Core/Content/ContentCatalog.cs) and [catalog.json](https://github.com/factoryseatlmuapp/factory-seat/blob/d0d4b81aa831f989e981b0d219856577438f6c15/src/LmuCareer.Core/Content/catalog.json): installed folders distinguish vehicle models/versions. Result CarType names can be missing or shared by different folder variants, so manufacturer or result model text is not a sufficient unique key. Bahrain contains layoutEndurance/layoutOuter; BMW_M4_LMGT3_2023 belongs to GT3.
- [RoundMatcher.cs](https://github.com/factoryseatlmuapp/factory-seat/blob/d0d4b81aa831f989e981b0d219856577438f6c15/src/LmuCareer.Core/Careers/RoundMatcher.cs): post-race matching prefers folder plus exact layout file; reused display names are explicitly unreliable. A 0.001 multiplier tolerance is used. Its result acceptance/near-miss rules are not copied into readiness: readiness requires affirmative verification of all applicable settings.
- [GuestDrives.cs](https://github.com/factoryseatlmuapp/factory-seat/blob/d0d4b81aa831f989e981b0d219856577438f6c15/src/LmuCareer.Core/Careers/GuestDrives.cs): guest rounds may use another team/car/class outside the season championship.
- [CareerStore.cs](https://github.com/factoryseatlmuapp/factory-seat/blob/d0d4b81aa831f989e981b0d219856577438f6c15/src/LmuCareer.Core/Careers/CareerStore.cs): default save root is `%AppData%\LmuCareer` or `LMUCAREER_HOME`; saves are under careers. This is context, **not** a confirmed briefing export path. The companion takes an explicit input path.

The source investigation found the relevant career/UI structures; no final briefing JSON schema was supplied. The provisional discriminator is independent of career schema version 1. The companion requires the eventual export to identify the effective round car rather than trying to infer it from potentially ambiguous model strings.

## LMU REST evidence

The user brief supplies experimental endpoint/field evidence. Exact original response bodies were not attached. The checked-in examples reconstruct the documented fields and explicitly label unobserved envelope/path/navigation choices as synthetic. A navigation GET to `127.0.0.1:6397` failed to connect during this run; no live menu or replay acceptance has been claimed.

[LMU WeatherPlugin source](https://github.com/geims12/LMU_WeatherPlugin/blob/main/WeatherPlugin.cs) independently demonstrates a local GET to GetSessionsInfoForEvent and reading scheduledSessions/lengthTime as minutes. That project's broader parsing/polling implementation is not a safety contract for ours. Other queried exact setting/navigation strings did not provide usable primary response captures.

Pending evidence: real sessions envelope and readable labels, numeric enum/index semantics, actual vehFile/fullPathTree format, setup/replay/loading navigation schema, car custom-team marker, class aliases if needed, and scene identifiers for other tracks. Unknown fields remain unknown rather than being guessed.

## Layout scene identifiers

Collected 2026-10-10 from an LMU install with every track owned. `GET /rest/race/track` lists each installed layout's `sceneDesc` with its display name, but not its location folder or layout file. 24 of the 38 layouts were tied to a layout file directly: each replay (`UserData\Replays\*.Vcr`) records the scene and location folder, and the results file written at the same time (`UserData\Log\Results\*.xml`, `TrackData`) names the layout `.mas`. Every scene paired with the same layout file in all sessions. The other 14 were matched by display name within their circuit, where the remaining layouts differ: Bahrain Paddock, Spa ELMS, Imola ELMS, COTA National, Lusail Short, Long Beach, Silverstone WEC/International/National and Paul Ricard 1A/1A-V2/1A-V2-Short/3A (Paul Ricard ELMS was confirmed from a results file). The `.mas` archives are encrypted, so their contents were not read. Factory Seat's owner later captured `cmpName` and `sceneDesc` for all 38 layouts from LMU's track picker; their list matches these mappings exactly, so all 38 are confirmed.

## SimHub

The official [Plugin and extensions SDKs](https://github.com/SHWotever/SimHub/wiki/Plugin-and-extensions-SDKs) describes the installed `PluginSdk/User.PluginSdkDemo` example and property/action extension scope. Inspected the installed `DataPluginDemo.cs`, `User.PluginSdkDemo.csproj`, and `GettingStarted.txt` location. The project targets .NET Framework 4.8 and demonstrates `IPlugin`, `IDataPlugin`, `IWPFSettingsV2`, `Init`, `End`, and `AttachDelegate` custom properties. Its comments require `DataUpdate` to remain fast and avoid exceptions. The existing local Lovely Car Data Capture source also demonstrates use of AttachDelegate, but is not a required dependency.

Proposed Phase 2 property adapter: overall ready/status and career/event/round; each row's Required, Current, Status and Reason; briefing/connection/freshness diagnostics. Use immutable report publication from a cancellable background task. Delegates read the latest report and enforce age limits even if no subsequent poll arrives. Avoid HTTP/file work in SimHub's telemetry callback, especially because telemetry may not flow while the setup menus are open.

The net8.0 core cannot be loaded directly by a net48 SimHub plugin. Before implementation, choose a compatible target/dependency strategy or an optional local process bridge. Do not invent a plugin DLL/dashboard package and call it usable without importing and testing it in the installed SimHub.

The official [Dash Studio Overlays](https://github.com/SHWotever/SimHub/wiki/Dash-Studio-Overlays) describes overlays as borderless windows and requires windowed or borderless game mode. LMU pre-race visibility, behavior while SimHub waits for data, dashboard export/import, practical dimensions, and rendering mode still need actual acceptance in Phase 2. No overlay has been installed or visually verified.
