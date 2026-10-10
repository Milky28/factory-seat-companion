# Factory Seat Companion

A SimHub overlay for **Le Mans Ultimate** that tells you, before you start a race, whether LMU's race setup matches the round your **Factory Seat** career expects. It reads the career's next round, reads LMU's current menu settings, and shows a checklist with one line per setting and a banner that reads **READY TO RACE** only when everything lines up.

It only reads. It never changes LMU's settings, never writes to Factory Seat or your career save, and its only game traffic is local HTTP reads from LMU on your own PC.

> **Status:** early and unpackaged. There is no download yet, so installing means building it from source (below). It has been run end to end on one PC; expect rough edges.

## What it checks

| Row | Compared against |
| --- | --- |
| Circuit and exact layout | The round's track and layout |
| Car model, class, number and team | The round's car (number and team only when the round sets them) |
| Race duration | The round's race length, when LMU's race finish is **Time**; otherwise a reminder to set it by hand (see below) |
| Fuel consumption, tyre wear, time scale | The round's multipliers |
| Race start time | The round's start time, or LMU's default |

A "Custom team identity" row appears when the round uses a Race Control custom team.

## Requirements

- Windows, with **Le Mans Ultimate** in windowed or borderless mode. SimHub overlays cannot draw over exclusive fullscreen.
- **SimHub** (tested with 9.13.2).
- **Factory Seat 1.1 or later**, which writes the open career's next round to `%AppData%\LmuCareer\briefing.json`. Earlier versions don't write this file, so the overlay stays at NOT VERIFIED. The format is described in [docs/briefing.md](docs/briefing.md).
- To build it: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and the [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48).

## Install

1. **Build and install the plugin.** Close SimHub (tray icon, Exit), then from a PowerShell window in the repository folder run:

   ```powershell
   ./scripts/Build-Plugin.ps1 -Install
   ```

   This builds the plugin and its companion program and copies both into `C:\Program Files (x86)\SimHub\`. If SimHub lives elsewhere, add `-SimHubPath "D:\path\to\SimHub\"`. You may need an administrator PowerShell to write into Program Files.

2. **Install the overlay.** Dash Studio's Import button only accepts packaged `.simhubdash` files, so copy the dashboard by hand:

   ```
   dashboard\FactorySeatCompanion.djson
     → C:\Program Files (x86)\SimHub\DashTemplates\FactorySeatCompanion\FactorySeatCompanion.djson
   ```

   Create the `FactorySeatCompanion` folder if it doesn't exist.

3. **Create a settings file.** Copy [`config/companion.example.json`](config/companion.example.json) somewhere outside the SimHub folder, for example `Documents\FactorySeatCompanion\companion.json`, and change `"safeSetupStates": []` to:

   ```json
   "safeSetupStates": ["GSTATE_SETUP"]
   ```

   This lets the checks run while you are in LMU's menus. They pause while you drive, watch a replay or load. The example ships with an empty list on purpose, because LMU reports its main menu and race setup screens identically; see [Known limits](#known-limits).

4. **Start SimHub once** and enable **Factory Seat Companion** when SimHub asks about the new plugin. Then exit SimHub from the tray icon so it saves the plugin's settings file.

5. **Point the plugin at your settings file.** With SimHub closed, open `C:\Program Files (x86)\SimHub\PluginsData\Common\FactorySeatPlugin.GeneralSettings.json` and set `ConfigPath` to the file from step 3 (use `\\` in JSON paths):

   ```json
   "ConfigPath": "C:\\Users\\you\\Documents\\FactorySeatCompanion\\companion.json"
   ```

   There is no settings screen yet. The other options are described in [docs/simhub-plugin.md](docs/simhub-plugin.md#settings).

6. **Start the overlay.** Start SimHub, open **Dash Studio**, go to **Overlays**, and start **FactorySeatCompanion**. Move it where you want it over LMU's menus.

## Using it

1. Open your career in Factory Seat. Leave Factory Seat running; it's how the companion knows the briefing is current.
2. Start LMU and set up the race in its single-player menus.
3. Watch the overlay while you change settings. Each row updates within a couple of seconds. When every row is green, the banner turns green and reads **READY TO RACE**.

### When the overlay shows

The overlay shows while you're in LMU's menus and hides as soon as an event starts loading. It stays hidden in the garage, while driving, in the pause menu and in replays, and comes back when you return to the menus. LMU can't tell the companion whether you're on the main menu or on race setup, so it shows on both.

To hide it on the main menu, bind a key or wheel button to it. In SimHub, open **Controls and events**, add a new mapping, press the button you want, and pick the action **FactorySeatPlugin.ToggleOverlay**. Pressing it hides the overlay until your next session, and pressing it again brings it back. If LMU isn't reachable, the overlay stays up so you can still see which settings the round needs.

## Reading the overlay

The top banner sums everything up:

| Banner | Meaning |
| --- | --- |
| **READY TO RACE** (green) | Every row matches or doesn't apply, and the data is fresh. |
| **SETUP MISMATCH** (red) | At least one LMU setting differs from the round. Fix the red rows. |
| **NOT VERIFIED** (amber) | Something can't be confirmed: a row is unknown, LMU or the briefing isn't readable, or the data is out of date. |

Next to the banner is the career, event and round, then one row per check showing what the round needs and what LMU currently has. Each row starts with a mark and is coloured by its status:

| Mark | Status | Meaning |
| --- | --- | --- |
| ✓ green | Match | LMU has what the round requires. |
| ✗ red | Mismatch | LMU has a different value. The row is highlighted, and a **To do:** line under the rows says what to change in LMU's words, such as "Set Tyre Wear to X3". |
| ○ amber | Unknown | The value couldn't be read or confirmed. This blocks READY TO RACE. |
| – grey | Any | The round doesn't restrict this setting, such as any car number. |
| ☐ blue | Check | LMU isn't reporting this value yet, so set it by hand. It doesn't block READY TO RACE. |

If LMU can't be read (for example after a game update breaks its API), every row shows ○ with the round's value still listed, and an amber note under the rows says to check each setting in LMU's menu by hand. The overlay then works as a plain checklist.

The footer shows where the data comes from. **Briefing** and **LMU** say whether each source is readable (`Available`, `Unavailable`, `Stale`, `Invalid` or `UnsafeState`). **Bridge** says whether the plugin's companion program is running. Any problem the checker found appears as a message at the bottom.

Common reasons for NOT VERIFIED:

- **Briefing Stale:** Factory Seat is closed, or you've left the career. Reopen it.
- **Briefing Unavailable:** Factory Seat hasn't written a briefing (version older than 1.1), or the career is between seasons.
- **LMU Unavailable:** LMU isn't running, or isn't answering on its local port yet.
- **LMU UnsafeState:** you're driving, in a replay or loading. Checks resume in the menus. If it never clears in the menus, check step 3 of Install.
- **Exact layout Unknown:** the companion doesn't know how this layout is named in LMU yet. See below.

## Known limits

- **Race duration in "% Track Time" mode is checked by hand.** In that mode LMU doesn't report a Race Time change until the hours change or the session starts. The row shows ☐ Check, and the **To do:** line reminds you to set the Race Time, for example "Set Race Time to 0 hrs 30 mins (change the hours once so LMU confirms it)". Changing the hours switches LMU to Time mode, and the row is then compared normally.
- **Layouts need a mapping.** LMU and Factory Seat name layouts differently, and only Bahrain (Endurance and Outer) and Lusail ship in the example settings. Any other layout shows Unknown. To add one, select the layout in LMU: the message at the bottom of the overlay then reads "No layout mapping for trackFolder …, sceneDescription …". Add both values with the round's `layoutFile` (from `briefing.json`) to `layoutMappings` in your settings file and restart SimHub.
- **Custom teams** can't be confirmed through LMU's API, so a round with a Race Control custom team always stays NOT VERIFIED.
- **Main menu and race setup look the same to the API.** With `GSTATE_SETUP` allowed, checks also run on LMU's main menu, and the overlay shows there too. That's harmless because LMU's own menus read the same data there. Use the toggle in [When the overlay shows](#when-the-overlay-shows) to hide it.
- **Overlay size.** SimHub refuses to start overlays above an undocumented size, so the overlay is fixed at 720×406.
- LMU's local API is unofficial and undocumented. A game update can change it and break checks, which show as Unknown rather than a false match.

## For developers

Building and testing the core, the diagnostic CLI and the live LMU test procedure are in [docs/development.md](docs/development.md). See also the [SimHub plugin internals](docs/simhub-plugin.md), [briefing format](docs/briefing.md), [architecture](docs/architecture.md) and [research notes](docs/research.md).

## License

[MIT](LICENSE)
