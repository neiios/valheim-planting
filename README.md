# BulkPlanting: plant whole fields at once in Valheim

Hold the Cultivator, pick a seed, and every click plants a full grid. The grid is
packed as tightly as the game allows, so every plant still grows.

- **Compact by default.** Hex packing fits about 15% more plants into the same soil
  than square rows. Spacing is computed for each crop from the game's own growth rule:
  the grow radius plus the size of the neighbouring sapling or grown crop.
  It isn't a guessed constant.
- **Neat fields.** When you aim next to crops you already planted, the grid snaps
  onto that field's lattice and orientation, so fields extend seamlessly. Aiming
  over an existing field only fills the gaps.
- **Live preview.** Every plant in the grid is previewed. Red means it would not
  grow there or you can't afford it. Possible reasons: soil not cultivated, a roof
  overhead, wrong biome, too cold or hot, an obstacle, crowding another sapling,
  no seeds, no stamina. Spots that already hold a plant are hidden.
- **Fair.** Each plant costs a seed, stamina and Cultivator durability, and gives
  Farming XP, exactly as planting by hand. All of this is configurable.

## Install on Windows

1. Close Valheim.
2. Download `BulkPlanting-<version>-windows.zip` from the
   [latest release](https://github.com/neiios/valheim-planting/releases/latest) and extract it.
3. Double-click **`Install.bat`**. If Windows SmartScreen warns you, click
   *More info → Run anyway*.
4. Start Valheim from Steam as usual. No launch options are needed on Windows.

Or paste this into PowerShell, which downloads and runs the same installer:

```powershell
irm https://raw.githubusercontent.com/neiios/valheim-planting/main/windows/install.ps1 | iex
```

The installer finds Valheim through Steam (any library folder), installs the
**BepInEx** mod loader if you don't already have it (pinned version, checksum
verified), and copies `BulkPlanting.dll` into `Valheim\BepInEx\plugins\BulkPlanting\`.
If it can't find the game, it asks for the folder. `Uninstall.bat` removes the mod again.

**r2modman / Thunderstore Mod Manager users:** skip the installer and drop
`BulkPlanting.dll` (also attached to each release) into your profile's `BepInEx\plugins` folder.

## Install on Linux (build from source)

Valheim mods run through **BepInEx**. The scripts below install it and build the mod.
They get all their tooling from [nix](https://nixos.org) (flakes enabled). The game is expected in Steam's default library
(`~/.local/share/Steam/steamapps/common/Valheim`); set `VALHEIM_DIR=/path/to/Valheim` otherwise.

```sh
git clone https://github.com/neiios/valheim-planting
cd valheim-planting
nix develop -c ./scripts/deploy.sh
```

This downloads BepInExPack_Valheim (pinned version and checksum) into the game
folder if it isn't there yet. Then it builds the mod and copies it to
`Valheim/BepInEx/plugins/BulkPlanting/`.

On Linux, Steam normally runs the Windows build of Valheim through Proton. Proton must be
told to load BepInEx's `winhttp.dll`. In Steam: **Valheim → Properties → General → Launch Options**:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

If you run the native Linux build instead (no Proton), use `./start_game_bepinex.sh %command%`
as the launch option.

Then **quit Valheim completely and start it again from Steam**.

### Check that it loaded

Open `BepInEx/LogOutput.log` in the Valheim folder, or on Linux:

```sh
grep -i bulkplanting ~/.local/share/Steam/steamapps/common/Valheim/BepInEx/LogOutput.log
```

You should see `BulkPlanting 1.0.0 loaded`. The main menu should also show the
game as modded. If the log file doesn't exist at all, BepInEx didn't start;
double-check the launch option.

## How to use

1. Equip the **Cultivator** and cultivate a patch of soil, as usual.
2. Right-click to open the build menu and pick a seed (carrot, turnip, barley, saplings…).
3. A status panel appears at the top of the screen, and a grid of ghost plants follows your cursor.
4. **Left-click** to plant the whole grid.

| Key | Action |
| --- | --- |
| `N` | Turn bulk planting on or off |
| `←` / `→` | Fewer or more columns |
| `↓` / `↑` | Fewer or more rows |
| `Left Alt` + mouse wheel | Shrink or grow the whole grid |
| Mouse wheel | Rotate the grid (vanilla rotation, 22.5° steps) |
| `H` | Switch between hex and square patterns |

The grid starts at your cursor (that's the middle of the nearest row) and extends
away from you. When the panel says **aligned to field**, the grid continues an
existing field. When you run low on seeds or stamina, the plants nearest the cursor
are planted first.

Vines (Ashlands) are left to vanilla placement, since they attach to walls.

## Configuration

After the first launch, settings live in
`Valheim/BepInEx/config/igorr.BulkPlanting.cfg`. Grid size and pattern are
remembered there. With a config manager mod (e.g. Official BepInEx ConfigurationManager)
you can change settings in-game with F1.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Rows`, `Columns` | 3, 3 | Grid size (1–12) |
| `Pattern` | Hex | `Hex` (densest) or `Square` (straight rows) |
| `SpacingMargin` | 0.05 | Extra meters on top of the tightest spacing at which plants still grow |
| `SnapToExistingPlants` | true | Align with neighbouring crops |
| `RequireGrowableSpot` | true | Only plant where the crop will actually grow |
| `StaminaPerPlant`, `DurabilityPerPlant`, `SkillPerPlant` | true | Charge or reward each plant like a hand-planted one |
| Keys section | see above | Rebind every key |
| `ShowHud`, `PanelVerticalPosition` | true, 0.06 | Status panel on/off and height (0 = top, 1 = bottom) |

## Multiplayer

Purely client-side. It works on vanilla servers and other players don't need it.
Plants are placed through the game's normal placement code.

## After a Valheim update

Rebuild against the new game files:

```sh
nix develop -c ./scripts/deploy.sh
```

If a big update renamed things the mod relies on, the build fails or
`LogOutput.log` shows a Harmony error. `ilspycmd` is included in the dev shell for
reading the game's code (`Player.UpdatePlacementGhost`, `Player.TryPlacePiece`,
`Plant.HaveGrowSpace`).

## Uninstall

- On Windows, `Uninstall.bat` from the release zip removes the mod.
- Just this mod: delete `Valheim/BepInEx/plugins/BulkPlanting/`.
- All modding: also delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `doorstop_libs/`,
  `changelog.txt`, `start_game_bepinex.sh`, `start_server_bepinex.sh` and `BepInEx/` from
  the Valheim folder, and clear the Steam launch option. Alternatively,
  "Verify integrity of game files" restores vanilla files but leaves the extra ones behind.

## Project layout

```
flake.nix                    dev shell: .NET 8 SDK, ilspycmd, curl, unzip
BulkPlanting/BulkPlanting.csproj
BulkPlanting/src/Plugin.cs        BepInEx entry point, config, key handling
BulkPlanting/src/BulkPlanter.cs   grid layout, growth validation, previews, placement
BulkPlanting/src/Patches.cs       Harmony hooks into Player placement
BulkPlanting/src/StatusPanel.cs   on-screen status panel
scripts/install-bepinex.sh   installs the mod loader into the game folder (Linux)
scripts/deploy.sh            build + copy the plugin into BepInEx/plugins (Linux)
scripts/package.sh           build the release files into dist/
windows/install.ps1          Windows installer (Install.bat / Uninstall.bat wrap it)
```

### Making a release

```sh
nix develop -c ./scripts/package.sh
gh release create vX.Y.Z dist/*
```

Bump `<Version>` in the csproj and `Version` in `Plugin.cs` first. The Windows
installer always fetches the DLL from the latest release.

The game assemblies are referenced from your Valheim install, with private members
publicized at build time, so nothing from the game is copied into this repo.
Use `VALHEIM_DIR=/path/to/Valheim` if the game lives somewhere else.

## License

GPL-3.0. See [LICENSE](LICENSE).
