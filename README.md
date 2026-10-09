# CyberTrap Launcher

A separate Windows launcher and installer for the [CyberTrap three-game crossover](https://github.com/dr4lera/CyberpunkStreetChem/releases/tag/v0.3.0): Cyberpunk 2077, Schedule I and Nivalis Nights.

## Download and use

Get the portable Windows x64 ZIP from [Releases](https://github.com/dr4lera/CyberTrapLauncher/releases). Extract the whole ZIP into a writable folder and open `CyberTrapLauncher.exe`. The launcher includes its own .NET runtime; keep the JSON files and `payload` folder alongside the EXE.

1. Install and run each game once. Schedule I and Nivalis must use their Windows IL2CPP builds (`GameAssembly.dll` present). Create a native save in each guest.
2. Use **Detect Steam** or browse to each game's EXE. Non-Steam installations work with manual selection.
3. Select a Nivalis native save. The installer creates a dedicated copy and preserves the source. An already configured business sandbox is imported instead. Schedule I defaults to its last played save, or select its `Game.json` explicitly.
4. With the games closed, choose **Install / Update**. Official framework and CyberTrap Release 3 downloads are pinned and checksum verified. Windows may request administrator access for protected game folders or MelonLoader's .NET 6 prerequisite.
5. Choose **Launch CyberTrap**. It starts all three games, loads the guest saves through their native loaders, resumes Schedule I and brings Cyberpunk forward. Load your paired Cyberpunk save normally. Keep the launcher open during play.

The progress bar shows download percentage when available and an activity indicator during setup/loading. First-time IL2CPP loader generation can take several minutes.

## Features

- Installs RED4ext, redscript, Codeware, TweakXL, MelonLoader, BepInEx and ReShade with full add-on support, plus the published crossover mods. Existing compatible framework installations are retained. Unrelated loader/graphics proxy conflicts stop installation with an explanation.
- Adds a small Schedule I launcher helper with a current-user-only named pipe. It loads/resumes a native save without modifying money or inventory.
- Optionally prepares the dedicated Nivalis copy: venue unlocks, recipes/menus, equipment, seating, service hours and native crew setup. Already prepared businesses are preserved. Ingredients use the crossover's real eddy reservations; costs and profits follow the business mod's rules.
- Conceals guests using transparent, click-through tool windows. They stay running and rendering. **Show background games** and closing the launcher restore them. After a launcher crash, run `CyberTrapLauncher.exe --restore-windows`.
- Refuses a different live guest save and blocks business-save changes while a payment is pending. Nivalis only operates while Cyberpunk renews its gameplay lease.

This solo crossover requires all three installed games. Cyberpunk save loading remains under your control. Your PC needs enough RAM/GPU capacity for three games; the launcher does not remove their resource costs. Steam must be available for Steam builds.

Tested with Cyberpunk 2.31, Schedule I 0.4.6f13 IL2CPP and Nivalis 1.0 patch 3 hotfix (Steam build 25738165). The pinned framework versions are listed in `frameworks.json`.

## Backups and troubleshooting

Settings, download cache, window recovery data and installation backups live under `%LOCALAPPDATA%\CyberTrapLauncher`. **Restore install** restores the latest installation's changed files with all games closed. Files edited since installation are retained. Repeat to restore earlier installations. Native saves and the system .NET prerequisite are not removed; use Windows Apps to uninstall a runtime if desired.

If loading stops, use **Show background games** and check the loaders' logs. No repeated Nivalis load command is sent after a timeout. Select a valid Schedule I save once in Continue if there is no last-played save. Keep using an existing configured sandbox rather than copying its paired financial history into a new pair.

Existing ReShade users need **full add-on support**. A graphics proxy belonging to another mod requires manual resolution. Other mods must be compatible with the pinned framework versions.

## Build

Requires .NET 10 SDK and an installed IL2CPP Schedule I with MelonLoader's generated assemblies:

```powershell
./tools/BuildRelease.ps1 -ScheduleIPath 'C:\Path\To\Schedule I'
```

This builds the original helper, publishes a self-contained Windows x64 launcher and creates a ZIP plus SHA256 file in `dist`. Retail assemblies are reference-only and are not packaged. `--self-test` runs path/configuration checks; `--install-test` uses disposable fixture folders to verify installation, payment guards and restoration.

## Credits and licenses

Original launcher/helper code: MIT, dr4lera / Codex (AI-assisted). Gameplay mods and Nivalis ModKit come from the separate [mod repository](https://github.com/dr4lera/CyberpunkStreetChem); see its credits/licenses. Frameworks retain upstream licenses: [RED4ext](https://github.com/WopsS/RED4ext), [redscript](https://github.com/jac3km4/redscript), [Codeware](https://github.com/psiberx/cp2077-codeware), [TweakXL](https://github.com/psiberx/cp2077-tweak-xl), [MelonLoader](https://github.com/LavaGang/MelonLoader), [BepInEx](https://github.com/BepInEx/BepInEx), [ReShade](https://reshade.me), and [Microsoft .NET](https://dotnet.microsoft.com). The downloaded ReShade shader include is CC0-1.0. URLs/checksums are in the shipped manifests.

No retail game files, native saves, credentials or private transaction journals are included.
