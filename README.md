# Factory Seat Companion

A SimHub overlay for **Le Mans Ultimate** that tells you, before you start a race, whether LMU's race setup matches the round your **Factory Seat** career expects. It reads the career's next round, reads LMU's current menu settings, and shows a checklist with one line per setting and a banner that reads **READY TO RACE** only when everything lines up.

It only reads. It never changes LMU's settings, never writes to Factory Seat or your career save, and its only game traffic is local HTTP reads from LMU on your own PC.

> **Status:** early. It has been run end to end on one PC; expect rough edges.

## What it checks

| Row | Compared against |
| --- | --- |
| Circuit and exact layout | The round's track and layout |
| Car model, class, number and team | The round's car (number and team only when the round sets them) |
| Race duration | The round's race length, when LMU's finish criteria is **Time** |
| Fuel consumption, tyre wear, time scale | The round's multipliers |
| Race start time | The round's start time, or LMU's default |

A "Custom team identity" row appears when the round uses a Race Control custom team.

## Requirements

- Windows, with **Le Mans Ultimate** in windowed or borderless mode. SimHub overlays cannot draw over exclusive fullscreen.
- **SimHub** (tested with 9.13.2).
- **Factory Seat 1.1 or later**, which writes the open career's next round to `%AppData%\LmuCareer\briefing.json`. Earlier versions don't write this file, so the overlay stays at NOT VERIFIED. The format is described in [docs/briefing.md](docs/briefing.md).

## Install

Download the latest `FactorySeatCompanion-Setup-<version>.exe` or `FactorySeatCompanion-<version>.zip` from the [Releases page](https://github.com/Milky28/factory-seat-companion/releases). Until a release is published, both are attached to each run of the [Player package workflow](https://github.com/Milky28/factory-seat-companion/actions/workflows/package.yml) as `FactorySeatCompanion-player` (open a green run and scroll to Artifacts; GitHub asks you to sign in).

1. **Exit SimHub** (tray icon, Exit).
2. **Run the installer.** It finds your SimHub folder and installs the plugin, its companion program (no .NET install needed), a ready settings file and the overlay. If SimHub lives somewhere unusual, pick the folder that contains `SimHubWPF.exe`.

   Or, with the zip: extract it into your SimHub folder (usually `C:\Program Files (x86)\SimHub\`) and let it merge the `FactorySeatCompanion` and `DashTemplates` folders.
3. **Start SimHub** and enable **Factory Seat Companion** when SimHub asks about the new plugin.
4. **Start the overlay.** Open **Dash Studio**, go to **Overlays**, and start **FactorySeatCompanion**. Move it where you want it over LMU's menus.

To update, run the newer installer the same way. To remove it, uninstall **Factory Seat Companion (SimHub plugin)** from Windows' Installed apps.

The installed settings let the checks run while you are in LMU's menus (`"safeSetupStates": ["GSTATE_SETUP"]`); they pause while you drive, watch a replay or load. To change settings, copy `SimHub\FactorySeatCompanion\companion.json` to `%AppData%\FactorySeatCompanion\companion.json` and edit that copy: it takes precedence and updates never overwrite it. Restart SimHub afterwards. Other plugin options are in [docs/simhub-plugin.md](docs/simhub-plugin.md#settings); building from source is in [docs/development.md](docs/development.md#build-and-install-from-source).

## Using it

1. Open your career in Factory Seat. Leave Factory Seat running; it's how the companion knows the briefing is current.
2. Start LMU and set up the race in its single-player menus.
3. Watch the overlay while you change settings. Each row updates within a couple of seconds. When every row is green, the banner turns green and reads **READY TO RACE**.

## Reading the overlay

The top banner sums everything up:

| Banner | Meaning |
| --- | --- |
| **READY TO RACE** (green) | Every row matches or doesn't apply, and the data is fresh. |
| **SETUP MISMATCH** (red) | At least one LMU setting differs from the round. Fix the red rows. |
| **NOT VERIFIED** (amber) | Something can't be confirmed: a row is unknown, LMU or the briefing isn't readable, or the data is out of date. |

Under it is the career, event and round, then one row per check showing what the round requires and what LMU currently has. The row's colour is its status:

| Colour | Status | Meaning |
| --- | --- | --- |
| Green | Match | LMU has what the round requires. |
| Red | Mismatch | LMU has a different value. Change it in LMU's menu. |
| Amber | Unknown | The value couldn't be read or confirmed. This blocks READY TO RACE. |
| Grey | Not applicable | The round doesn't restrict this setting, such as any car number. |

The footer shows where the data comes from. **Briefing** and **LMU** say whether each source is readable (`Available`, `Unavailable`, `Stale`, `Invalid` or `UnsafeState`). **Bridge** says whether the plugin's companion program is running. Any problem the checker found appears as a message at the bottom.

Common reasons for NOT VERIFIED:

- **Briefing Stale:** Factory Seat is closed, or you've left the career. Reopen it.
- **Briefing Unavailable:** Factory Seat hasn't written a briefing (version older than 1.1), or the career is between seasons.
- **LMU Unavailable:** LMU isn't running, or isn't answering on its local port yet.
- **LMU UnsafeState:** you're driving, in a replay or loading. Checks resume in the menus. If it never clears in the menus, check that your settings file still has `"safeSetupStates": ["GSTATE_SETUP"]`.
- **Exact layout Unknown:** the companion doesn't know how this layout is named in LMU yet. See below.

## Known limits

- **Race duration in "% Track Time" mode** stays Unknown. LMU's API doesn't report the menu's race length in that mode. Set the finish criteria to **Time** to have it checked.
- **Layouts need a mapping.** LMU and Factory Seat name layouts differently, and only Bahrain (Endurance and Outer) and Lusail ship in the installed settings. Any other layout shows Unknown. To add one, select the layout in LMU: the Circuit row's LMU value is the `trackFolder`, and the Exact layout row's LMU value is the `sceneDescription`. Add both with the round's `layoutFile` to `layoutMappings` in your `%AppData%\FactorySeatCompanion\companion.json` (see Install) and restart SimHub.
- **Custom teams** can't be confirmed through LMU's API, so a round with a Race Control custom team always stays NOT VERIFIED.
- **Main menu and race setup look the same to the API.** With `GSTATE_SETUP` allowed, checks also run on LMU's main menu. That's harmless because LMU's own menus read the same data there, but it means the companion can't tell you which screen you're on.
- **Overlay size.** SimHub refuses to start overlays above an undocumented size, so the overlay is fixed at 720×406.
- LMU's local API is unofficial and undocumented. A game update can change it and break checks, which show as Unknown rather than a false match.

## For developers

Building and testing the core, the diagnostic CLI and the live LMU test procedure are in [docs/development.md](docs/development.md). See also the [SimHub plugin internals](docs/simhub-plugin.md), [briefing format](docs/briefing.md), [architecture](docs/architecture.md) and [research notes](docs/research.md).

## License

[MIT](LICENSE)
