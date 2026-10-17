# Factory Seat briefing

Factory Seat writes the open career's next race to `%AppData%\LmuCareer\briefing.json` (or `LMUCAREER_HOME\briefing.json` when that variable is set). The CLI reads that path by default; `--briefing <path>` overrides it. `FactorySeatBriefingReader` accepts `format: "factory-seat-briefing"` with `version: 1`, rereads the file every cycle, and ignores fields it does not know, as the format allows. `fixtures/briefing.spec-example.json` is the example from Factory Seat's spec.

## Live or stale

The file stays after the player leaves the career or closes Factory Seat, so the reader decides whether it describes the career on screen:

| Briefing | Result |
| --- | --- |
| `open: false` | Stale: the last briefing is shown, every row is Unknown |
| `open: true`, no `FactorySeat` process running | Stale: a crash or forced shutdown left `open` true |
| `open: true`, Factory Seat running, round `Upcoming` or `Armed` | Available |
| `round: null` | Unavailable: between seasons, nothing to set up |
| Wrong `format`, other `version`, missing `open`, other round state, duplicate properties, malformed JSON | Invalid |

Fixture mode (`--fixtures`) skips the process check because it is synthetic end to end.

## Field mapping

| Briefing field | Check | Notes |
| --- | --- | --- |
| `career.name`, `round.event.name`, `round.number` | Header | Required for readiness |
| `round.track.folder` | Circuit | Compared with LMU `cmpName` |
| `round.track.layoutFile` | Exact layout | Via the configured `(cmpName, sceneDesc)` layout mapping |
| `round.car.folder` | Car model | Compared with the `Installed\Vehicles` folder in the selected `.VEH` path; `null` (car not known) stays Unknown |
| `round.car.class` | Car class | Whole class token in LMU's class list |
| `round.car.number` | Car number | `null` means any livery (NotApplicable); `customTeam: true` also allows any number |
| `round.car.team` | Team | Compared when set; `null` or empty is not compared. With `customTeam: true` it is not compared either (NotApplicable): LMU reports the generic car name, such as "Lexus Custom Team 2026" |
| `round.car.customTeam` | Custom team identity | `true` adds a row shown as Not checkable (NotApplicable): REST has no custom-team marker, so it doesn't block readiness. A missing value keeps the row Unknown |
| `round.settings.raceMinutes` | Race duration | Compared when LMU's finish criteria is Time; otherwise a non-blocking manual check |
| `round.settings.fuelUsage`, `tyreWear`, `timeScale` | Fuel, tyres, time scale | LMU factors: 1 is Real/Normal, otherwise xN |
| `round.settings.startTime` | Race start time | `HH:mm`; `null` means LMU's default (NotApplicable) |
| `round.pace.step`, else `round.practice.aiStrengthStep` (1.2) | AI Strength | Advice, shown as a manual row (Check) worded as a change, e.g. "Raise 3%", with a "Raise AI Strength 3%" hint; `0` reads "Keep as is". Factory Seat can't see LMU's slider and LMU doesn't report it, so it never blocks readiness. No row when neither is present |
| `round.stakes.lines` (1.2) | Overlay stakes line | The first two lines, most important first; not a check |

A missing field is never a default: it leaves its row Unknown and blocks readiness. `liveryNumbers`, `carTypes`, `pitPlan`, `sponsors` and `writtenAt` are read by nothing yet.
