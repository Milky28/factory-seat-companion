# Provisional development briefing

**This is an internal test format, not a proposed final Factory Seat save/export contract.** Its required discriminator is `contract: "factory-seat-companion-provisional"` and supported `schemaVersion` is 1. A Factory Seat career save is deliberately rejected even if it also has schema version 1. See `fixtures/briefing.provisional.json` for a complete example.

`IFactorySeatReader` returns normalized requirements and source health. Replace `ProvisionalJsonBriefingReader` with a final-format adapter once the creator supplies the export. Preserve explicit unknowns and omission policies; do not make missing fields default to 1, the first number, the season car, or an unrestricted livery.

| Field | Development meaning |
| --- | --- |
| generatedAt | UTC/offset timestamp of this briefing; absent, future by >30 seconds, or too old prevents readiness |
| careerName, eventName, roundNumber | Required header metadata, round number positive |
| state | Exactly Upcoming or Armed for setup validation; Completed/Skipped not accepted |
| trackFolder, layoutFile | Factory Seat catalog identifiers; never substitute a display name |
| raceMinutes | Positive minutes, compared with the unique RACE scheduled session lengthTime |
| fuelMultiplier, tireMultiplier | Nonnegative physical factors; zero can represent Off |
| timeScale | Positive physical factor |
| startTime | HH:mm or supported 12-hour readable time |
| startTimeRequired | true: validate the supplied time; false with absent startTime: explicit default/unrestricted; missing or conflicting policy: Unknown |
| car.folder, car.class | Effective round vehicle's installed folder and exact class token |
| car.number | Locked race number, takes precedence over permittedNumbers |
| car.permittedNumbers | Allowed selection before a locked number exists; empty alone does not mean any livery |
| car.anyNumber | true explicitly permits any number; combine with absent number/empty permittedNumbers |
| car.customTeam | Must be explicit. true permits any number but introduces a mandatory, currently unverifiable custom-team identity check |
| car.teamRequired | true: compare team; false: explicitly omit team comparison; missing: Unknown |
| car.team | Required team value only when teamRequired=true; otherwise may be informational |
| car.liveryName | Optional exact livery requirement; supplied value adds a mandatory check |

For a guest round, the fixture/export must contain its guest car and guest permitted numbers. The reader does not inspect season saves to discover them. For an unrestricted first livery use `anyNumber=true`, `teamRequired=false`, `customTeam=false`, absent `number`, and empty permitted numbers. For custom teams, do not pretend a team-name match proves Race Control custom-team identity.

Outstanding contract questions for the creator: schema identifier/version and update policy, briefing path and lifetime, active career/round identity, effective guest car's installed folder, class aliases, locked vs permitted numbers, custom-team marker, team requirement vs informational contract name, default start time, timestamp semantics, and event/round completion invalidation. None of these requires changing Factory Seat career saves.
