# CyberTrap Launcher (development)

Separate all-in-one launcher and mod/framework installer for Cyberpunk 2077, Schedule I and Nivalis Nights.

**Current state: UI scaffold and pinned framework download metadata. Not yet runnable or buildable: application code, installation and launch coordination are still missing. No playable launcher release is available.**

The playable crossover mods remain in the [StreetChem mod repository](https://github.com/dr4lera/CyberpunkStreetChem) and [CyberTrap Release 3](https://github.com/dr4lera/CyberpunkStreetChem/releases/tag/v0.3.0).

## Planned

- Steam installation discovery and manual EXE selection.
- Verified mod/framework installation with backups and restoration.
- Dedicated Nivalis save copies, preserving original saves.
- Native guest save loading and bridge activation.
- Background guests without taskbar entries, tested against passthrough rendering before enabling by default.
- CyberTrap-themed launcher with visible progress and troubleshooting controls.

## Files

- `MainWindow.xaml`, `App.xaml`: proposed Windows WPF interface.
- `CyberTrapLauncher.csproj`, `app.manifest`: Windows project scaffold.
- `frameworks.json`: pinned official download URLs and SHA256 hashes for the currently tested frameworks. This metadata does not itself install anything.

MIT license for original code. Frameworks retain their upstream licenses. No retail game files, saves, credentials or private transaction journals are included.
