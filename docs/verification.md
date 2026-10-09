# Phase 1 verification

Verified in this workspace on 2026-10-09 using .NET SDK 8.0.421 / Windows:

- Release solution build: zero warnings and zero errors. Single-process MSBuild with shared compilation disabled avoided an initial sandbox build failure that emitted no compiler diagnostics.
- Executable automated tests: 63/63 passed (after the 2026-10-09 live-test fixes). Includes every baseline missing/mismatched field, permitted/locked numbers, custom and guest identity, explicit omission policies, malformed/duplicate JSON, unknown layouts, staleness, file refresh/deletion/recovery, timeout/cancellation, oversized responses, loopback GET-only requests, and navigation replay transitions.
- `scripts/Run-Demo.ps1`: a fresh copied briefing and synthetic responses matched all eleven baseline rows; exit 0.
- CLI structured output: Fixture mode explicitly identified and complete report emitted; exit 0.
- CLI offline live mode: all checks unknown, LMU unavailable, connection diagnostic emitted; exit 2. The .NET sandbox denied socket access in that smoke test. A separate curl navigation attempt also could not connect. Neither is live LMU compatibility evidence.
- Invalid zero polling interval rejected with a clear configuration diagnostic; exit 1.

No real LMU response capture, replay endpoint safety test in-game, or SimHub overlay visibility test was possible during this run. Tests use the real HTTP client orchestration with a fake HttpMessageHandler; they do not substitute for testing the game's unsupported API. No Factory Seat career save or LMU setting was modified.
