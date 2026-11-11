Keeps up with Factory Seat 1.3 and makes sure your race counts.

- **Start race weekend reminder.** A race only counts in Factory Seat if it finishes after you press **Start race weekend**. When LMU's setup matches but the weekend isn't started, the banner now reads **START RACE WEEKEND** instead of READY TO RACE, and the To do line says "Press Start race weekend in Factory Seat".
- **Test days (Factory Seat 1.3).** On a test day you drive practice laps in another team's car, so the race settings don't apply. The overlay now checks only the circuit, layout and car, reads **READY TO TEST** when they match, and the line under the checklist says what the team wants, such as "Test day for Toyota Racing: 8 laps in practice, within 0.5% of the fastest AI lap in class." Before, it showed the full race checklist and could flag settings that didn't matter.
- **IMSA rounds (Factory Seat 1.3).** When a round counts for IMSA rather than the season's own championship, the headline says so, for example "Round 3 (IMSA)".
- **New SimHub properties** for your own dashboards: `FactorySeatPlugin.Weekend` (`Started`, `Not started` or `Unknown`) and `FactorySeatPlugin.TestDay`.

Works with Factory Seat 1.1 to 1.4. The overlay size is unchanged, so there's nothing to redo in SimHub after updating.
