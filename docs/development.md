# Development and live testing

This is the developer guide: building and testing the core, running the diagnostic CLI, and checking it against a running LMU. Players should start with the [README](../README.md).

## Run the proof of concept

Requires the .NET 8 SDK. Run these commands from the repository root:

```powershell
dotnet build FactorySeatCompanion.sln -c Release -m:1 /p:UseSharedCompilation=false
dotnet run --project tests/FactorySeatCompanion.Tests -c Release --no-build
./scripts/Run-Demo.ps1
```

The demo reads `fixtures/briefing.json` and reports `READY TO RACE` using **synthetic** LMU observations. There are no NuGet dependencies. The tests are an executable assertion runner, so use `dotnet run`, **not `dotnet test`**. Any failed test returns exit code 1.

For continuous fixture updates:

```powershell
dotnet run --project src/FactorySeatCompanion.Cli -c Release --no-build -- --briefing fixtures/briefing.json --fixtures fixtures/lmu --config config/companion.example.json
```

Edit a copied fixture directory to explore mismatches. File changes are reread every cycle. Ctrl+C stops the process. `--json` outputs structured reports and includes `mode: Fixture` or `mode: Live`; output is a sequence of indented JSON documents, not JSONL.

## Test against a running LMU

1. Start LMU and enter its single-player race setup menus. First inspect navigation only:

   ```powershell
   dotnet run --project src/FactorySeatCompanion.Cli -c Release --no-build -- --inspect-navigation
   ```

2. Copy `config/companion.example.json` to your own configuration. Identify the exact JSON property path containing the menu state. Set `navigationStatePointer` to that JSON pointer and `safeSetupStates` to the exact state values observed **only in safe pre-race setup screens**. Compare main menu, loading, driving, and replay navigation responses using `--inspect-navigation` before allowing a state. Do not use `SYNTHETIC_SETUP_ONLY` from the test fixture. If the response lacks a reliably exclusive setup state, leave the allowlist empty and do not enable garage polling. In the 2026-10-09 live test, `state` was an object, and the main menu and race settings reported identical values (`gameState` `GSTATE_SETUP`, `navigationState` `NAV_MAIN_MENU`), so no state uniquely identifies the setup screens. The example therefore points at `/state/gameState` with an empty allowlist. LMU's own menu reads `garage/UIScreen/SessionSetup` throughout the main-menu area in that state, so the owner chose (2026-10-09) to allow `GSTATE_SETUP` in a local config: checks then run anywhere in LMU's menus, while `GSTATE_DYN` (driving and replay) and loading stay blocked.

3. Open the career in Factory Seat so it writes `%AppData%\LmuCareer\briefing.json`, which the CLI reads by default. Then run:

   ```powershell
   dotnet run --project src/FactorySeatCompanion.Cli -c Release --no-build -- --config "C:\path\to\companion.local.json"
   ```

4. Change duration, fuel, tyre wear, time scale, and starting time one at a time. Confirm each row changes. Select Bahrain Endurance then Outer: the exact layout row must mismatch. Switch model, class, number, and team. Confirm manufacturer alone never produces a match. Check any new layout mapping against both `cmpName` and `sceneDesc`.
5. Enter a replay and confirm `UnsafeState`, all rows unknown, and no garage polling. Close LMU and confirm `Unavailable`; reopen and return to the allowed menu to confirm recovery. Leave the career in Factory Seat and confirm the briefing turns `Stale` with every row Unknown; reopen it to recover.

Polling uses a one-second delay after each completed cycle, not overlapping fixed-rate callbacks. A successful cycle currently performs three navigation GETs and one GET each for settings, duration, and setup. Each request has a two-second default deadline, so disconnected/slow cycles can take longer than one second. Configure `pollSeconds` from 1 to 60 and `requestTimeoutSeconds` from 0.1 to 10. CLI overrides include `--poll-seconds` and `--timeout-seconds`.

For a single diagnostic result, add `--once`. Exit codes: 0 means all applicable checks were verified; 2 means mismatch, unknown, stale or unavailable; 1 means a usage/configuration/inspection error. Normal unavailable sources in continuous mode do not terminate the process.

## Status and limitations

- `Match`: identified values agree. `Mismatch`: known values differ. `Unknown`: absent, invalid, ambiguous, unsafe, stale, or unsupported evidence. `NotApplicable` (shown as `Any`, greyed on the overlay): the briefing explicitly allows any value, such as any car number or LMU's default start time. Each `Mismatch` row also carries a one-line `hint` in LMU's menu wording, for example `Set Tyre Wear to X3`.
- Readiness requires valid briefing metadata, current sources, and every applicable row matching. Manual acknowledgement is not implemented.
- Eleven baseline rows cover the ten requested categories by showing number and team separately. A custom-team row appears when needed.
- `SESSSET_race_time` is ignored. RACE `lengthTime` supplies minutes only when `SESSSET_Finish_Criteria` reads `Time`. In `% Track Time` mode, live LMU showed 2h24 in the menu while `lengthTime` was 48 or 24, so duration stays unknown. Readable multiplier labels and 12/24-hour times are normalized. Raw setting `currentValue` is deliberately not interpreted without captured evidence of its numeric/enum semantics.
- Only the two Bahrain layout mappings supplied in the project brief are included. New/ambiguous layouts stay unknown.
- A custom-team car cannot currently be proven through REST. Such a briefing always remains unverified until a reliable custom-team marker is identified.
- A first live test on 2026-10-09 parsed all four endpoints and matched the menus, except duration in `% Track Time` mode. It found no navigation value that separates the setup screens from the main menu, and no replay signal: replay and driving responses were identical. Live LMU reports loading as `loadingStatus.loading`, which is checked; it sends no `isReplay`/`isLoading`. The poll checks navigation around garage access, but the unsupported API cannot make multiple requests atomic or eliminate all state-transition races.
- A briefing is current only while its `open` flag is true and a `FactorySeat` process is running; there is no age limit.

See [architecture and implementation plan](architecture.md), [source and SimHub investigation](research.md), and [fixture provenance](../fixtures/README.md). Phase 2 must produce an actual imported Dash Studio overlay and verify visibility over LMU menus; the console demo is Phase 1 only.
