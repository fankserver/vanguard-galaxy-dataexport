# VGDataExport

A BepInEx 5.x plugin for [Vanguard Galaxy](https://store.steampowered.com/) that dumps the live game's catalogue — ships, turrets, armor modules, aspects, factions, conquest ranks — to JSON, plus a Python tool that merges the dump into the [Vanguard Galaxy Wiki](https://vanguard-galaxy.fandom.com/) data modules.

The wiki's `Module:ShipData` is generated from these dumps; whenever a game patch changes HP scales, hardpoint layouts, requirements, or rank tables, you can regenerate the module without losing hand-curated fields like lore notes or `notForSale` flags.

## What it dumps

Each export pass writes the following files to `BepInEx/dataexport/`:

| File | Contents |
|---|---|
| `vg-data.json` | Combined envelope of everything below. |
| `ships.json` | Per-ship prefab catalogue: class, manufacturer, HP scales, hardpoint layout, warp speed/acceleration, cargo, tonnage, crew slots, sell value, full `ShopRequirements` (level / rep / conquest), and a `HasCommander` flag for pilotable hulls. |
| `turrets.json` | Turret prefab tuning (fire delay, reload, mag size, intrinsic stats, power multiplier). |
| `reference-instances.json` | Rolled instances at configured (level, rarity) combinations — captures CombatPower / MiningPower / SalvagePower at typical reference points. |
| `armor-modules.json` | ArmorModule prefab tuning (resist amount, weak amount, weak types, capacity overrides). |
| `aspects.json` | EquipAspect prefabs and their BoostStat lines. |
| `factions.json` | Faction id → display name + description, plus per-faction conquest rank names (e.g. Stellar Industries Rank2 = "Associate", Marauders Rank2 = "Cutthroat"). |
| `player.json` | Currently-active ship snapshot (turrets equipped, rolled stats). Only present when a save is loaded. |

## Installation

1. Build (see below) or grab a prebuilt `VGDataExport.dll`.
2. Drop the DLL into `<game>/BepInEx/plugins/VGDataExport/`.
3. Launch the game and load a save.

The plugin auto-exports a few seconds after a savegame loads. You can also force a dump at any time with `Ctrl+Shift+F10` (configurable via the `Hotkey` setting).

## Configuration

`BepInEx/config/vgdataexport.cfg` (created on first run):

| Setting | Default | Notes |
|---|---|---|
| `AutoExport` | `true` | Run a single dump once a savegame is loaded. |
| `AutoDelaySeconds` | `5` | Extra delay after the player ship appears, so prefabs settle. |
| `OutputDir` | `BepInEx/dataexport` | Path is relative to the game install root unless absolute. |
| `Hotkey` | `F10` | Pressing `Ctrl+Shift+<key>` re-runs the dump. |
| `Levels` | `10,20,30` | Item levels to roll reference turret instances at. |
| `Rarities` | `Standard,Enhanced,HighGrade` | Rarities to roll reference instances at. |

## Building

The project targets `netstandard2.1` and references the publicized `Assembly-CSharp.dll` stub from a sibling [VGTTS](https://github.com/fankserver/vanguard-galaxy-tts) plugin (see `Makefile`).

```bash
make build      # links libs, builds VGDataExport.dll
make deploy     # copies the DLL into <GAME_DIR>/BepInEx/plugins/VGDataExport/
make clean
```

If `dotnet` isn't on PATH, the Makefile falls back to a pre-staged SDK at `/tmp/dnsdk/dotnet/dotnet`. Adjust `GAME_DIR` at the top of the `Makefile` to match your local install (Steam path differs per platform).

## Wiki integration (`tools/merge_shipdata.py`)

The companion Python script merges a `ships.json` + `factions.json` pair into the wiki's `Module:ShipData`, overlaying live game fields and preserving authored fields (`notes`, `notForSale`, `image`, `displayName`, etc.).

```bash
python3 tools/merge_shipdata.py \
  --ship-data /tmp/ShipData_full.lua \
  --ships /path/to/dataexport/ships.json \
  --factions /path/to/dataexport/factions.json \
  --output /tmp/ShipData_merged.lua
```

The script prints a per-ship diff for every field the live overlay would change. See `tools/README.md` for the merge rules and per-faction conquest rank translation.

## License

MIT — see `LICENSE`.
