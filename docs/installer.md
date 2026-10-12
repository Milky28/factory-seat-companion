# Player download and installer (Phase 3)

Players no longer need to build from source or edit SimHub's settings file. Each CI run of the
**Player package** workflow attaches a `FactorySeatCompanion-player` artifact with two files:

- `FactorySeatCompanion-Setup-<version>.exe`: the installer.
- `FactorySeatCompanion-<version>.zip`: the same files laid out like the SimHub folder, for players who prefer to copy them by hand.

## What gets installed

Into the SimHub folder (found from SimHub's uninstall entry, else `C:\Program Files (x86)\SimHub`; setup refuses a folder without `SimHub.Plugins.dll`):

| File | Purpose |
| --- | --- |
| `FactorySeatCompanion.SimHub.dll` | The plugin. |
| `FactorySeatCompanion\FactorySeatCompanion.Cli.exe` | The companion, self-contained, so no .NET install is needed. |
| `FactorySeatCompanion\companion.json` | Ready config from `config/companion.player.json`: `/state/gameState` with `GSTATE_SETUP` allowed (the owner's 2026-10-09 decision) and the known layout mappings. |
| `DashTemplates\FactorySeatCompanion\FactorySeatCompanion.djson` | The overlay. Any cached `.djson.metadata` is deleted so a new size takes effect. |

Setup never touches `SimHub\OverlayLayouts`, so a saved layout's position and visibility (`AutoMode` in the `.olayout`) survive an update. SimHub's "overlays are forced visible" toggle is runtime state with no saved setting, so it resets on every SimHub restart, including the one an update needs. Setup asks the player to exit SimHub first. Afterwards: start SimHub, enable Factory Seat Companion when asked, then start the overlay from Dash Studio's Overlays list. Uninstall from Windows' Installed apps removes these files only.

## Configuration

With the plugin's `ConfigPath` setting empty (the default), the plugin uses, in order:

1. `%AppData%\FactorySeatCompanion\companion.json`, a player's own copy that updates never overwrite.
2. `FactorySeatCompanion\companion.json` next to the CLI, the installed default that each update refreshes.

A `ConfigPath` set in `SimHub\PluginsData\Common\FactorySeatPlugin.GeneralSettings.json` (as in a manual install) still wins. The briefing default is unchanged: Factory Seat's `%AppData%\LmuCareer\briefing.json`.

## Building it

```powershell
./scripts/Package.ps1 -Version 0.1.0          # needs SimHub, .NET 8 SDK, .NET Framework 4.8 Developer Pack, Inno Setup 6
./scripts/Package.ps1 -NoInstaller            # zip only
```

CI installs SimHub silently into a scratch folder (`scripts/Get-SimHubSdk.ps1`) only to compile against its DLLs; no SimHub file is packaged. The self-contained CLI publish fetches the .NET runtime pack from nuget.org; the project itself still has no NuGet dependencies.

To publish a release, run the **Release** workflow by hand (Actions > Release > Run workflow) with a version such as `0.1.0-beta.1`. It builds from the chosen branch, creates the tag `v<version>` and a GitHub release (a pre-release unless unticked) with the installer and zip attached. The notes come from `.github/release-notes.md`, with this release's bullet points from `.github/release-highlights.md`; update that file first. Re-running for an existing version refreshes its notes and files. Nothing is released automatically.
