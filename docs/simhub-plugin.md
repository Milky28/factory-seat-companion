# SimHub plugin and overlay (Phase 2, untested in SimHub)

Status (2026-10-09): built against SimHub 9.13.2, loaded without errors, and the overlay showed READY TO RACE in fixture mode on the owner's PC. Live LMU acceptance is still outstanding.

## How it fits together

```mermaid
flowchart LR
    C[FactorySeatCompanion.Cli --jsonl] -- one report per line --> B[CompanionBridge]
    B --> D[DashboardState]
    D --> P[FactorySeatPlugin properties]
    P --> O[Dash Studio overlay]
```

SimHub loads .NET Framework 4.8 plugins, and the core targets net8.0, so the plugin cannot load it in-process. Retargeting the core would need System.Text.Json and its dependencies inside SimHub's process, where SimHub ships its own assembly versions. Instead the plugin starts the existing CLI as a child process (the local process bridge option from [architecture](architecture.md)). Validation rules stay in one place, the core.

- The CLI runs with `--jsonl --exit-when-stdin-closes`. Each line is the same `{mode, report}` document as `--json`, compacted. The plugin keeps the CLI's stdin open, so the CLI stops when SimHub exits.
- `CompanionBridge` restarts the CLI 5 s after it exits and clears the last report whenever the CLI is not running or prints an unreadable line.
- `DashboardState` fails closed. It is READY TO RACE only if the latest report says so, every row is Match or NotApplicable, all rows fit the 13 slots, and the report's `observedAt` is within `ReportMaxAgeSeconds` (default 10). A stale report keeps the required values but shows LMU values as Unknown.
- Properties are computed when read (at most every 250 ms), so age limits apply even when no game telemetry flows. `DataUpdate` does nothing.

## Properties

All are prefixed `FactorySeatPlugin.`

| Property | Meaning |
| --- | --- |
| `ReadyToRace` | Boolean. True only for a fresh, fully matching report. |
| `OverallStatus` | `READY TO RACE`, `SETUP MISMATCH` or `NOT VERIFIED`. |
| `Headline` | Career / event / round. |
| `Mode` | `Fixture` or `Live`. |
| `Screen` | `Menu`, `Loading`, `InSession` or `Unknown`, from the latest fresh report. |
| `ShowOverlay` | Boolean. Bind the overlay's visibility to it; see [When the overlay shows](#when-the-overlay-shows). |
| `BriefingStatus`, `LmuStatus` | Source health from the report, or `Stale`. |
| `ReportAgeSeconds` | Age of the report's observation. |
| `BridgeStatus` | `Running`, or why the companion is not running. |
| `Diagnostics` | Report diagnostics joined with ` \| `. |
| `Hints` | Fix hints of every mismatched row, joined with ` · `. Empty when nothing mismatches or the report is stale. |
| `ManualCheck` | A note to check each setting by hand, set when the briefing is available but LMU's settings can't be read or the report is stale. Otherwise empty. |
| `Row01.` … `Row13.` + `Visible`, `Id`, `Label`, `Required`, `Current`, `Status`, `StatusText`, `Icon`, `Reason`, `Hint` | One slot per check row in report order. Unused slots have `Visible` false. `StatusText` is `Status` with `NotApplicable` shown as `Any`; `Icon` is ✓, ✗, – (Any) or ○ (Unknown); `Hint` is set only on a fresh `Mismatch` row. |

## When the overlay shows

The CLI classifies each poll's `/navigation/state` (`LmuScreenClassifier` in the core) and the `--jsonl` line carries it as `screen`. Values from the live probe on 2026-10-10 (LMU v1.4200, notes in the project's live-test findings):

| LMU screen | navigation/state | `Screen` |
| --- | --- | --- |
| Main menu, race setup | `GSTATE_SETUP` / `NAV_MAIN_MENU` (identical) | `Menu` |
| Loading | `loadingStatus.loading` true, `GSTATE_INIT` / `GSTATE_RESTART` | `Loading` |
| In-event garage/monitor | `NAV_EVENT` | `InSession` |
| Driving, pause, replay | `NAV_REALTIME` | `InSession` |
| Leaving a session | `GSTATE_POST` | `InSession` |
| LMU not reachable, anything else | | `Unknown` |

LMU's menu is a self-contained web app that never reports its page, so nothing separates race setup from the main menu. `ShowOverlay` is true for `Menu` and `Unknown` (a stale report counts as `Unknown`, so the checklist stays up when the API breaks) and false for `Loading` and `InSession`. The `FactorySeatPlugin.ToggleOverlay` action flips that until LMU reaches a different known screen, so hiding it on the main menu lasts until the next session. Fixture mode reports `Menu`.

Replay can be told apart from driving with `GET /rest/replay/isActive`, but both hide the overlay, so the CLI doesn't call it.

## Settings

There is no settings screen yet (Phase 3). SimHub stores the plugin's common settings as `GeneralSettings` JSON in its plugin data folder after the first run; edit it with SimHub closed:

- `BriefingPath`: a briefing file to use instead of the CLI's default (Factory Seat's `%AppData%\LmuCareer\briefing.json`).
- `ConfigPath`: companion configuration, as for the CLI.
- `FixturesPath`: synthetic fixture folder, to test the overlay without LMU. Shows `Mode: Fixture`.
- `CliPath`: defaults to `FactorySeatCompanion\FactorySeatCompanion.Cli.exe` next to the plugin DLL.
- `ReportMaxAgeSeconds`: default 10.

## Build and install

Needs Windows, SimHub, the .NET 8 SDK and the .NET Framework 4.8 Developer Pack. The plugin project is not in `FactorySeatCompanion.sln`, so CI builds without SimHub.

```powershell
./scripts/Build-Plugin.ps1                 # builds into artifacts/simhub
./scripts/Build-Plugin.ps1 -Install        # also copies into SimHub (close SimHub first)
```

Then start SimHub and enable Factory Seat Companion when it asks about the new plugin. Dash Studio's Import button only takes packaged `.simhubdash` files, so copy `dashboard/FactorySeatCompanion.djson` to `SimHub\DashTemplates\FactorySeatCompanion\FactorySeatCompanion.djson` and start it from Dash Studio's Overlays list.

SimHub refuses to start overlays above an undocumented pixel area ("This overlay is too large"). 900×580 was refused and 900×390 runs, so the overlay is 720×406. SimHub caches the size in a `.djson.metadata` file next to the dashboard; delete it after resizing.

The plugin saves its settings only when SimHub exits cleanly (tray icon, Exit). The file is `SimHub\PluginsData\Common\FactorySeatPlugin.GeneralSettings.json`.

## Acceptance

Steps 1, 2 and 4 passed in fixture mode on 2026-10-09; 3 and 5 are outstanding.

1. SimHub loads the plugin and lists `FactorySeatPlugin.*` in the property picker.
2. With `FixturesPath` set to `fixtures/lmu` and a fresh copy of the provisional briefing, the overlay shows READY TO RACE and Mode Fixture. Editing a fixture value turns that row red within a few seconds.
3. Kill the CLI process: the overlay drops to NOT VERIFIED within `ReportMaxAgeSeconds` and recovers after the restart.
4. Dash Studio imports the `.djson` (it was written by hand from the Dash Studio format and may need fixing on import). Check text sizes and the overlay size.
5. Live: LMU in windowed or borderless mode, overlay visible over the race setup menus, including while SimHub waits for telemetry. Then repeat the live checks in [development](development.md#test-against-a-running-lmu).
