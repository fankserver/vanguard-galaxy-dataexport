# tools/merge_shipdata.py

Merges a fresh VGDataExport dump into the wiki's `Module:ShipData` while preserving every authored field.

## Usage

```bash
python3 merge_shipdata.py \
  --ship-data /path/to/current/ShipData.lua \
  --ships    /path/to/dataexport/ships.json \
  --factions /path/to/dataexport/factions.json \
  --output   /path/to/ShipData_merged.lua
```

Output is the rewritten Lua module. The script prints a per-ship diff to stdout for every field the live overlay would change, plus a summary on stderr.

## Live fields (overwritten on every run)

| ShipData field | Source |
|---|---|
| `prefab` | `ships.json` `PrefabName` |
| `manufacturer` | `Manufacturer` (skipped on `*-Marade` keys — vendor tagging) |
| `class` | `ClassName` (singular → plural map) |
| `crew` | `MaxOfficers` |
| `cargo` | `CargoCapacity` (raw `_cargoCapacity`, not the virtual sentinel) |
| `tonnage` | `Tonnage` |
| `speed` / `accel` | `MaxWarpSpeed` / `WarpAcceleration` × 100 (game stores raw units) |
| `hullScale` / `shieldScale` / `armorScale` | live HP scales |
| `sizeWeight` / `hardpoints` / `sizeSummary` | hardpoint geometry |
| `playerLevel` / `shipyardLevel` / `shipyardRep` / `shipyardFaction` / `conquestRank` | `ShopRequirements` (faction id → display name; rank id → per-faction rank name) |

## Authored fields (never touched)

`displayName`, `image`, `sizeBand`, `aux`, `notes`, `notForSale`, plus any future fields you add by hand. The merger doesn't need to know what they are — anything not in the live allowlist is preserved verbatim.

## Special-case behaviour

- **`notForSale = true`**: skips the entire shop overlay. Used for story rewards (Eclipse, Terravex), drones, and NPC-only hulls. The prefab carries a populated `shopItemData`, but the ship never appears in any shipyard stock — the in-game numbers are fictional and shouldn't be published.
- **`*-Marade` keys**: keep their authored manufacturer (Marade Wharf — the *vendor*) instead of being overwritten with the dump's *original builder*. The wiki tags Marade variants by who sells them, not who built them.
- **`playerLevel`**: emits `"0!"` when the requirement is zero, distinct from a true `"1+"`.
- **`shipyardLevel`**: emits `"min - max"` when there's an upper bound, else `"min+"`.
- **`conquestRank`**: translated per-faction (`Stellar Industries` Rank2 → `"Associate"`, `Marauders` Rank2 → `"Cutthroat"`).
- **Faction display names**: `Blue` → `"Stellar Industries"`, etc. Falls back to the raw enum id when the game's translation table has no entry (cut content).

## Workflow

1. Boot the game with VGDataExport installed, load any save.
2. Wait for the auto-export, or hit `Ctrl+Shift+F10`.
3. Fetch the current `Module:ShipData` from the wiki.
4. Run this script.
5. Eyeball the diff. Investigate any unexpected changes (a stat might have changed in a game patch, or a manual override might be drifting).
6. Publish the merged Lua back to `Module:ShipData`.

The script is idempotent — running it twice in a row against the same input produces no further changes.
