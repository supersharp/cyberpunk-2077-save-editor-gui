# CP2077 Save Editor

A fast, modern save editor for **Cyberpunk 2077, patch 2.5**. It is a rebuild of
[CyberCAT-SimpleGUI by Deweh](https://github.com/Deweh/CyberCAT-SimpleGUI) (Nexus mod 718), which stopped working
when patch 2.5 changed the save format. This version reads and writes 2.5 saves **losslessly** and adds a new
interface with real item names and icons.

![Save browser](docs/screenshots/saves.png)

## Highlights

- **Works with patch 2.5 saves.** Every save is re-written byte-for-byte identical when nothing was changed,
  including the new 2.5 stats format.
- **Save browser.** All your saves as cards with screenshot, quest, level, life path and play time. Search by
  quest, slot or life path. Keep your own save library outside the game folder (import saves, or show any other
  folder), and copy or move a save into the game's saves so it appears in Load Game.
- **Safe saving.** Before writing, a summary shows exactly which parts of the save changed. Save to a new slot
  (recommended), or overwrite with an automatic backup in `Documents\CP2077 Save Editor\Backups`.
- **Real names and icons from your own game.** On first start the editor finds your Cyberpunk 2077 install
  (Steam, GOG or Epic) and reads item names and icons from your game files. Nothing is downloaded and nothing from
  the game is shipped with the editor.
- **Player.** Life path, level, Street Cred, attributes, skills, money, and quick presets (max everything, max
  attributes, refund attributes…).
- **Inventory.** Every container (V's inventory, car stash…), filters, search, 2.0 tiers (Tier 1 to Tier 5++),
  quantities, quest-item flag, remove items, "upgrade all", and an **Add item** catalog of the game's items:
  - sub-filters (pistols, SMGs, katanas… clothing slots, cyberware by body area),
  - one entry per item with all its grades (add one grade, several, or all of them at once),
  - large previews (click an icon), and Phantom Liberty items locked for saves without the expansion.
- **Appearance.** Edit appearance values, and save or load appearance presets (old `.preset` files still load).
- **Vehicles.** Every player vehicle in your game version with its picture and brand. Unlock or lock one or all,
  mark favorites, filter by cars, motorcycles, unlocked or locked.
- **Quest facts.** Search, edit, add, remove and export quest facts, filter by main story, side jobs, gigs,
  romance or Phantom Liberty, see and undo your edits, name suggestions when adding a fact, plus shortcuts for the
  secret ending and making every romance available.
- **Extras.**
  - Toggle the patch 2.5 **"modded save" flag** (the cog icon on the Load Game screen).
  - Remove the **Phantom Liberty** flag from a save, and optionally remove Phantom Liberty items.
  - Allow Phantom Liberty items for saves without the expansion (at your own risk).
  - Unlock all fast travel points, fix a V stuck invulnerable, clear the Extra Wardrobe Slots blacklist.
- **Info and downloads.** Version information, credits for the projects this editor builds on, and a link to
  download new releases in your browser.

| Player | Inventory |
| --- | --- |
| ![Player](docs/screenshots/player.png) | ![Inventory](docs/screenshots/inventory.png) |
| **Add item** | **Extras** |
| ![Add item](docs/screenshots/add-item.png) | ![Extras](docs/screenshots/extras.png) |

## Download and use

1. Download **CP2077SaveEditor.exe** from the [latest release](https://github.com/supersharp/cyberpunk-2077-save-editor-gui/releases/latest).
   It is a single file: no install, no .NET runtime, put it anywhere.
2. Run it, pick a save, make changes, then press **Save changes** (or Ctrl+S).
3. To update, use **Download latest version** on the Info page, close the editor, and replace the old exe with the new download.

Prefer to build it yourself? The release page also has the source code, or see [Building from source](#building-from-source).

The first start takes about a minute in the background to read item names and icons from your game. After that
the editor starts instantly. The data is rebuilt automatically after a game update.

> **Always keep a backup.** The editor makes one for you when you overwrite a save, but save editing can still
> break a playthrough if you set values the game does not expect.

### Experimental

- The **modded save flag** and **Phantom Liberty** options are based on save research but have not yet been
  confirmed against every kind of save. Use them on a copy of your save first.
- Phantom Liberty items are found by their record names, so a few items may be missed.

## What changed for patch 2.5

Patch 2.5 changed how the game stores stats and some world data. The updates
(natively supported via WolvenKit's `update_2.5` branch) are:

- New stats format: `gameSavedStatsDataOptimized`, `gameBufferSlice` and the shared stat modifier buffer in
  `gameStatsStateMapStructure`, with read, edit and write support (`OptimizedStatsStore`).
- Fields that the game only sometimes writes are now tracked, so they are written back exactly as they were read.
- Extra trailing bytes in some world-state entries are preserved.
- New enum values (`ArmsCWStyle`).
- World-state entries are decoded only when needed, which makes opening a save about 30 times faster.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) and Git on Windows.

```powershell
git clone --recurse-submodules https://github.com/supersharp/cyberpunk-2077-save-editor-gui.git
cd cyberpunk-2077-save-editor-gui
./tools/setup.ps1                       # initializes the WolvenKit submodule
dotnet build CP2077SaveEditor.Wpf -c Release
dotnet publish CP2077SaveEditor.Wpf -c Release -r win-x64 -o publish/wpf   # single-file release
```

Releases are built by GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml)): pushing a
tag such as `v1.1.2` builds the single-file exe and zip package and publishes it as a release.

Projects:

| Project | What it is |
| --- | --- |
| `CP2077SaveEditor.Wpf` | The new editor (WPF, MVVM) |
| `CP2077SaveEditor.Core` | Save reading/writing, editing logic and game data, shared by both apps |
| `CP2077SaveEditor` | The original WinForms editor, kept working on top of the same core |
| `WolvenKit` | Submodule (`update_2.5` branch) that does the actual save parsing |

Build the projects one at a time; the upstream `ResourceGenerator` project in the solution does not build.

## Credits

- **[CP2077 Save Editor](https://github.com/supersharp/cyberpunk-2077-save-editor-gui) by supersharp**:
  Intelligent fuzzy search across all views and editors, .NET 10 x64 modernization, multi-branch WolvenKit
  release workflows, and clean WolvenKit update_2.5 integration.
- **[CP2077 Save Editor (Upstream)](https://github.com/AtfastrSlushyMaker/cyberpunk-2077-save-editor-gui) by AtfastrSlushyMaker**:
  The modern WPF rebuild with patch 2.5 save support, lossless writing, catalog indexing, vehicle pictures, and save library.

- **[CyberCAT-SimpleGUI](https://github.com/Deweh/CyberCAT-SimpleGUI) by Deweh**: the original editor this
  project is based on. All the original features and research are theirs.
- **[WolvenKit](https://github.com/WolvenKit/WolvenKit) by the WolvenKit team**: save, archive and game database
  parsing.
- **[CyberCAT](https://github.com/WolvenKit/CyberCAT) by SirBitesalot and contributors**: the original save
  research.
- Item names and icons are read from **your own copy of the game** through WolvenKit. They belong to CD PROJEKT
  RED.

Cyberpunk 2077 is a trademark of CD PROJEKT S.A. This project is not affiliated with or endorsed by CD PROJEKT RED.

## License

The editor's own source code is under the [MIT license](LICENSE) (original copyright Deweh). Release builds
include WolvenKit, which is licensed under **GPL-3.0**, so the distributed executable as a whole is under GPL-3.0.
Third-party licenses are in [`licenses/`](licenses).
