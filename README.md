# Factory Seat Companion for SimHub

Phase 1 is implemented: a read-only .NET 8 core and diagnostic CLI that compare a **provisional** briefing with LMU menu settings. It includes synthetic fixtures, automated tests, freshness checks, connection recovery, and configurable polling. A SimHub plugin/dashboard is **not implemented yet**.

The real Factory Seat `briefing.json` contract is pending. This application does not read career saves, infer a next round from them, or write into Factory Seat or LMU. Its only game requests are loopback HTTP GETs. No Factory Seat assemblies or assets are required.

## Run the proof of concept

Requires the .NET 8 SDK. Run these commands from this directory:

```powershell
dotnet build FactorySeatCompanion.sln -c Release -m:1 /p:UseSharedCompilation=false
dotnet run --project tests/FactorySeatCompanion.Tests -c Release --no-build
./scripts/Run-Demo.ps1
```

The demo refreshes the timestamp in a copy at `demo/briefing.json` and reports `READY TO RACE` using **synthetic** LMU observations. It never edits the checked-in fixture or an external briefing. The committed timestamp intentionally ages out; use the demo script for a fresh example. There are no NuGet dependencies. The tests are an executable assertion runner, so use `dotnet run`, **not `dotnet test`**. Any failed test returns exit code 1.

For continuous fixture updates:

```powershell
dotnet run --project src/FactorySeatCompanion.Cli -c Release --no-build -- --briefing demo/briefing.json --fixtures fixtures/lmu --config config/companion.example.json
```

Edit a copied fixture directory to explore mismatches. File changes are reread every cycle. Ctrl+C stops the process. `--json` outputs structured reports and includes `mode: Fixture` or `mode: Live`; output is a sequence of indented JSON documents, not JSONL.

## Test against a running LMU

1. Start LMU and enter its single-player race setup menus. First inspect navigation only:

   ```powershell
   dotnet run --project src/FactorySeatCompanion.Cli -c Release --no-build -- --inspect-navigation
   ```

2. Copy `config/companion.example.json` to your own configuration. Identify the exact JSON property path containing the menu state. Set `navigationStatePointer` to that JSON pointer and `safeSetupStates` to the exact state values observed **only in safe pre-race setup screens**. Compare main menu, loading, driving, and replay navigation responses using `--inspect-navigation` before allowing a state. Do not use `SYNTHETIC_SETUP_ONLY` from the test fixture. If the response lacks a reliably exclusive setup state, leave the allowlist empty and do not enable garage polling.

3. Supply a development briefing conforming to the [provisional adapter](docs/provisional-briefing.md). Until the creator supplies the final export schema, maintain this separate test file yourself. Do not point the reader at a `.career.json` file. Then run:

   ```powershell
   dotnet run --project src/FactorySeatCompanion.Cli -c Release --no-build -- --briefing "C:\path\to\development-briefing.json" --config "C:\path\to\companion.local.json"
   ```

4. Change duration, fuel, tyre wear, time scale, and starting time one at a time. Confirm each row changes. Select Bahrain Endurance then Outer: the exact layout row must mismatch. Switch model, class, number, and team. Confirm manufacturer alone never produces a match. Check any new layout mapping against both `cmpName` and `sceneDesc`.
5. Enter a replay and confirm `UnsafeState`, all rows unknown, and no garage polling. Close LMU and confirm `Unavailable`; reopen and return to the allowed menu to confirm recovery. Replace, truncate, and remove the separate development briefing to confirm that earlier matches are revoked. Refresh `generatedAt` to recover from a stale briefing.

Polling uses a one-second delay after each completed cycle, not overlapping fixed-rate callbacks. A successful cycle currently performs three navigation GETs and one GET each for settings, duration, and setup. Each request has a two-second default deadline, so disconnected/slow cycles can take longer than one second. Configure `pollSeconds` from 1 to 60 and `requestTimeoutSeconds` from 0.1 to 10. CLI overrides include `--poll-seconds`, `--timeout-seconds`, and `--briefing-max-age-minutes`.

For a single diagnostic result, add `--once`. Exit codes: 0 means all applicable checks were verified; 2 means mismatch, unknown, stale or unavailable; 1 means a usage/configuration/inspection error. Normal unavailable sources in continuous mode do not terminate the process.

## Status and limitations

- `Match`: identified values agree. `Mismatch`: known values differ. `Unknown`: absent, invalid, ambiguous, unsafe, stale, or unsupported evidence. `NotApplicable`: the briefing explicitly permits omission.
- Readiness requires valid briefing metadata, current sources, and every applicable row matching. Manual acknowledgement is not implemented.
- Eleven baseline rows cover the ten requested categories by showing number and team separately. Custom-team and exact-livery rows appear when needed.
- `SESSSET_race_time` is ignored. RACE `lengthTime` supplies minutes. Readable multiplier labels and 12/24-hour times are normalized. Raw setting `currentValue` is deliberately not interpreted without captured evidence of its numeric/enum semantics.
- Only the two Bahrain layout mappings supplied in the project brief are included. New/ambiguous layouts stay unknown.
- A custom-team car cannot currently be proven through REST. Such a briefing always remains unverified until a reliable custom-team marker is identified.
- No live LMU responses were captured in this workspace: `127.0.0.1:6397` was unavailable during development. Fixture shape compatibility and safe navigation states require live acceptance. The poll checks navigation around garage access, but the unsupported API cannot make multiple requests atomic or eliminate all state-transition races.
- The age limit defaults to 120 minutes from the briefing's `generatedAt`; a still relevant briefing can expire, intentionally requiring refresh. Confirm the final export's timestamp semantics before adapting it.

See [architecture and implementation plan](docs/architecture.md), [source and SimHub investigation](docs/research.md), and [fixture provenance](fixtures/README.md). Phase 2 must produce an actual imported Dash Studio overlay and verify visibility over LMU menus; the console demo is Phase 1 only.
