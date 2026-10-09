# Fixture provenance

All fixtures are **synthetic development examples**, not captured LMU responses or the creator's final JSON contract. Do not use them as proof of live compatibility.

- `briefing.json`: a version 1 Factory Seat briefing written to match the synthetic LMU fixtures (Bahrain Endurance, BMW M4 LMGT3 #31). Career and event values are made up.
- `briefing.spec-example.json`: the example from Factory Seat's briefing.json spec, unchanged.
- `lmu/sessions.json`: supplied setting identifiers, reconstructed top-level envelope and representative readable labels. `currentValue` values are illustrative, not evidence of numeric encoding. The parser does not use them.
- `lmu/durations.json`: documented scheduledSessions/name/lengthTime fields, 20-minute practice and 360-minute race.
- `lmu/setup.json`: supplied Bahrain and BMW identity values, reconstructed sample vehicle path and class-string representation. `id`, filename and livery string are placeholders. Factory Seat catalog confirms the installed folder/class, not this raw REST path representation.
- `lmu/navigation.live-shape.synthetic.json`: hand-written in the shape seen in the 2026-10-09 live test (object `state`, `loadingStatus.loading`). Values are from the main menu, which LMU cannot tell apart from race settings.
- `lmu/navigation.setup|replay.synthetic.json`: deliberately fake state token and navigation shape. Exercises a configured allowlist, replay checks and embedded JSON. Never add that state to a real game policy.

Add sanitized *actual* captures later with game version, screen/state, capture date, and expected interpretation. Avoid storing personal career saves, full player names or sensitive paths. Use live navigation captures to establish a safe policy before capturing garage responses.
