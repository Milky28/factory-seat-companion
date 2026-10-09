# Fixture provenance

All fixtures are **synthetic development examples**, not captured LMU responses or the creator's final JSON contract. Do not use them as proof of live compatibility.

- `briefing.provisional.json`: intentionally separate development schema. Identifier values reflect the supplied brief and examined Factory Seat catalog. Its timestamp is fixed for deterministic tests; `scripts/Run-Demo.ps1` refreshes a copy only.
- `lmu/sessions.json`: supplied setting identifiers, reconstructed top-level envelope and representative readable labels. `currentValue` values are illustrative, not evidence of numeric encoding. The parser does not use them.
- `lmu/durations.json`: documented scheduledSessions/name/lengthTime fields, 20-minute practice and 360-minute race.
- `lmu/setup.json`: supplied Bahrain and BMW identity values, reconstructed sample vehicle path and class-string representation. `id`, filename and livery string are placeholders. Factory Seat catalog confirms the installed folder/class, not this raw REST path representation.
- `lmu/navigation.*.synthetic.json`: deliberately fake state token and navigation shape. Exercises a configured allowlist, replay checks and embedded JSON. Never add that state to a real game policy.

Add sanitized *actual* captures later with game version, screen/state, capture date, and expected interpretation. Avoid storing personal career saves, full player names or sensitive paths. Use live navigation captures to establish a safe policy before capturing garage responses.
