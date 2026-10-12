**Factory Seat Companion** is a SimHub overlay for Le Mans Ultimate. Before you start a race, it checks LMU's race setup against the next round of your [Factory Seat](https://github.com/factoryseatlmuapp/factory-seat) career and shows a checklist that reads **READY TO RACE** only when everything matches. It only reads: it never changes LMU's settings or your career.

![The overlay showing READY TO RACE](https://raw.githubusercontent.com/Milky28/factory-seat-companion/main/docs/images/overlay-ready-to-race.png)

## What's in this release

{{HIGHLIGHTS}}

## Which file to download

- **FactorySeatCompanion-Setup-{{VERSION}}.exe**: the installer. Recommended.
- **FactorySeatCompanion-{{VERSION}}.zip**: the same files, to extract into your SimHub folder by hand.

## Install

1. Exit SimHub (tray icon, Exit) and run the installer. It finds your SimHub folder and installs the plugin, its companion program (no .NET install needed), a ready settings file and the overlay.
2. Start SimHub and enable **Factory Seat Companion** when it asks about the new plugin.
3. In **Dash Studio > Overlays**, set **Layout automatic visibility** to **Manual** and click **Save layout**. SimHub's default hides overlays in LMU's menus. The overlay still hides itself while you drive.
4. Start the **FactorySeatCompanion** overlay, open your career in Factory Seat, and set up the race in LMU.

**Updating from an earlier version:** exit SimHub and run the new installer. Your overlay layout and its Manual visibility are kept. Forcing overlays visible in SimHub isn't saved, so if you used that instead of step 3, do it again after SimHub restarts.

Full instructions and troubleshooting are in the [README](https://github.com/Milky28/factory-seat-companion#readme).

## Requirements

- Windows 10 or 11 (64-bit), SimHub (tested with 9.13.2), and LMU in windowed or borderless mode.
- **Factory Seat 1.1 or later**, which writes the round briefing (`briefing.json`). With older versions the overlay stays at NOT VERIFIED.

## Known limits

- **Race duration in "% Track Time" mode** is a manual check until you change the Race Time once and back (for example 30 to 45 and back to 30 mins). LMU then reports it and the row is compared normally.
- **Main menu and race setup look the same to LMU's API**, so the overlay also shows on the main menu. Hide it there with the toggle action.
- **Layouts added after October 2026** show Unknown until a mapping is added (see the README). All 38 current layouts are mapped.
- **Custom teams** can't be confirmed through LMU's API. On a custom team round the overlay can read READY TO RACE with Custom team identity shown as Not checkable, so check you picked your custom team's car yourself.
- LMU's local API is unofficial; a game update can break checks, which then show Unknown rather than a false match.
