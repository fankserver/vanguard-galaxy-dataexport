#!/usr/bin/env python3
"""
Merge live VGDataExport output into the wiki's Module:ShipData.

Reads:
  - The current Module:ShipData source (Lua), as the authoritative copy of all
    authored fields (notes, notForSale, image, displayName, hand-curated values).
  - ships.json + factions.json from a recent VGDataExport dump.

Writes a merged Lua file with:
  - Live fields overwritten from the dump (HP scales, hardpoints, manufacturer,
    class, crew/cargo/tonnage, warp speed/accel, shipyard requirements,
    conquest rank — translated to per-faction display names).
  - All other fields preserved verbatim.

Prints a per-ship diff for every field the live overlay would change.

Usage:
  merge_shipdata.py --ship-data SHIPDATA.lua \\
                    --ships ships.json --factions factions.json \\
                    --output ShipData_merged.lua
"""
from __future__ import annotations
import argparse
import json
import re
import sys
from pathlib import Path


# Class singular -> plural (wiki convention).
CLASS_PLURAL = {
    'Cutter': 'Cutters', 'Gunship': 'Gunships', 'Corvette': 'Corvettes',
    'Frigate': 'Frigates', 'Destroyer': 'Destroyers',
    'Mining Skiff': 'Mining Skiffs', 'Hewer': 'Hewers', 'Dredger': 'Dredgers',
    'Breaker': 'Breakers', 'Harvester': 'Harvesters',
    'Salvage Skiff': 'Salvage Skiffs', 'Scow': 'Scows', 'Scrapper': 'Scrappers',
    'Wrecker': 'Wreckers', 'Reclaimer': 'Reclaimers',
    'Courier': 'Couriers', 'Ferry': 'Ferries', 'Hauler': 'Haulers',
    'Freighter': 'Freighters', 'Carrack': 'Carracks',
}

# Fields the wiki's Module:Shipbox renders for shop requirements; we strip all
# of these from the overlay when a ship is flagged notForSale.
SHOP_FIELDS = ('playerLevel', 'shipyardLevel', 'shipyardRep',
               'shipyardFaction', 'conquestRank')


def load_faction_data(path: Path) -> tuple[dict[str, str], dict[tuple[str, str], str]]:
    """Returns (id->display_name, (id, rank_enum)->rank_display_name)."""
    if not path.exists():
        return {}, {}
    names: dict[str, str] = {}
    ranks: dict[tuple[str, str], str] = {}
    for f in json.loads(path.read_text()):
        fid = f['Identifier']
        n = f.get('Name') or ''
        # `Translation.Translate` returns the raw "@..." key when the entry has
        # no display name; treat those as "use the identifier".
        names[fid] = fid if n.startswith('@') else n
        for r in f.get('ConquestRanks') or []:
            ranks[(fid, r['Rank'])] = r['Name']
    return names, ranks


def overlay(key: str, dump: dict, faction_names: dict[str, str],
            rank_names: dict[tuple[str, str], str]) -> dict:
    """Return {ShipData_field: new_value} the live dump dictates."""
    out: dict = {}
    out['prefab'] = dump['PrefabName']
    # Vendor-tagging: -Marade slugs represent ships sold by Marade Wharf.
    # The dump always reports the *original* builder, so leave manufacturer
    # alone for those keys — the existing authored value (Marade Wharf) wins.
    if dump.get('Manufacturer') and not key.endswith('-Marade'):
        out['manufacturer'] = dump['Manufacturer']
    if dump.get('ClassName'):
        out['class'] = CLASS_PLURAL.get(dump['ClassName'], dump['ClassName'])
    out['crew']    = dump.get('MaxOfficers') or 0
    out['cargo']   = dump.get('CargoCapacity') or 0
    out['tonnage'] = dump.get('Tonnage') or 0
    if dump.get('MaxWarpSpeed') is not None:
        # Game uses raw units (0.38 ls/s); wiki convention multiplies by 100.
        out['speed'] = round(dump['MaxWarpSpeed'] * 100)
    if dump.get('WarpAcceleration') is not None:
        out['accel'] = round(dump['WarpAcceleration'] * 100)
    out['hullScale']   = dump.get('HullHPScale')   or 0
    out['shieldScale'] = dump.get('ShieldHPScale') or 0
    out['armorScale']  = dump.get('ArmorHPScale')  or 0
    out['sizeWeight']  = dump.get('EquivalentTurretsByConvention') or 0

    hp_codes = []
    for h in dump.get('Hardpoints', []):
        sz = h.get('Size', '')
        hp_codes.append({'Small': 'S', 'Medium': 'M', 'Large': 'L'}.get(sz, '?'))
    out['hardpoints'] = hp_codes

    summary = dump.get('SizeSummary') or {}
    out['sizeSummary'] = {'S': summary.get('Small', 0),
                          'M': summary.get('Medium', 0),
                          'L': summary.get('Large', 0)}

    sr = dump.get('ShopRequirements')
    if sr:
        lvl = sr.get('LevelRequirement') or 0
        # Wiki convention: "0!" marks "no level req at all" vs a true "1+".
        out['playerLevel']   = "0!" if lvl <= 0 else str(int(lvl))
        min_lvl = sr.get('MinAreaLevel') or 0
        max_lvl = sr.get('MaxAreaLevel') or 0
        out['shipyardLevel'] = (f"{int(min_lvl)} - {int(max_lvl)}"
                                if max_lvl > 0 else f"{int(min_lvl)}+")
        prereqs = sr.get('FactionPrereqs') or []
        if prereqs:
            fp = prereqs[0]
            out['shipyardRep'] = fp.get('ReputationLevel')
            fid = fp.get('Faction')
            out['shipyardFaction'] = faction_names.get(fid, fid) if fid else None
            cq = fp.get('ConquestRank')
            if cq in (None, 'None', ''):
                out['conquestRank'] = None
            else:
                out['conquestRank'] = rank_names.get((fid, cq), cq) if fid else cq
        else:
            out['shipyardRep']     = None
            out['shipyardFaction'] = None
            out['conquestRank']    = None
    return out


# ---------- Lua parser (purpose-built for the wiki's emit format) ----------
ENTRY_RE = re.compile(r'\[\s*"([^"]+)"\s*\]\s*=\s*\{', re.S)
FIELD_RE = re.compile(r'^(\s*)(\w+)\s*=\s*(.*?),\s*(?:--[^\n]*)?$', re.M)


def lua_str(s: str) -> str:
    return '"' + str(s).replace('\\', '\\\\').replace('"', '\\"') + '"'


def lua_num(n) -> str:
    if isinstance(n, int):
        return str(n)
    if float(n).is_integer():
        return str(int(n))
    return f'{n:g}'


def parse_entries(text: str) -> list[tuple[str, int, str, int]]:
    """Return [(key, header_start, body_text, body_end)] in source order."""
    entries = []
    for m in ENTRY_RE.finditer(text):
        key = m.group(1)
        depth = 1
        i = m.end()
        while i < len(text) and depth > 0:
            c = text[i]
            if c == '{': depth += 1
            elif c == '}': depth -= 1
            i += 1
        body = text[m.end():i - 1]
        entries.append((key, m.start(), body, i))
    return entries


def parse_fields(body: str) -> list[tuple[str, str, str]]:
    """Return [(indent, name, value_lua_text)] in source order."""
    return [(m.group(1), m.group(2), m.group(3).rstrip())
            for m in FIELD_RE.finditer(body)]


def emit_field_value(name: str, val) -> str:
    """Emit Lua for a known field, respecting type conventions."""
    if val is None:
        return 'nil'
    if name == 'hardpoints':
        return '{' + ', '.join(lua_str(c) for c in val) + '}'
    if name == 'sizeSummary':
        return '{' + ', '.join(f'{k}={int(v)}' for k, v in val.items()) + '}'
    if name in ('crew', 'cargo', 'tonnage', 'speed', 'accel',
                'hullScale', 'shieldScale', 'armorScale', 'sizeWeight'):
        return lua_num(val)
    return lua_str(val)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--ship-data', required=True, type=Path,
                        help='Path to current Module:ShipData (Lua source).')
    parser.add_argument('--ships', required=True, type=Path,
                        help='Path to ships.json from VGDataExport.')
    parser.add_argument('--factions', required=True, type=Path,
                        help='Path to factions.json from VGDataExport.')
    parser.add_argument('--output', required=True, type=Path,
                        help='Where to write the merged Lua file.')
    args = parser.parse_args()

    src = args.ship_data.read_text()
    dump = json.loads(args.ships.read_text())
    faction_names, rank_names = load_faction_data(args.factions)

    by_prefab = {s['PrefabName']: s for s in dump}
    by_displayname: dict[str, list] = {}
    for s in dump:
        by_displayname.setdefault(s['DisplayName'], []).append(s)

    entries = parse_entries(src)
    print(f"Parsed {len(entries)} entries from {args.ship_data}", file=sys.stderr)

    header = src[:entries[0][1]].rstrip() + '\n'
    # entries[-1][3] points just after the last entry's closing '}', BEFORE the
    # comma the source uses to separate entries. We emit our own comma per entry,
    # so strip a leading comma + whitespace from the trailer to avoid `},,`.
    trailer = re.sub(r'^\s*,', '', src[entries[-1][3]:])

    chunks = [header]
    n_changed = n_unchanged = 0
    no_match: list[str] = []

    for key, _, body, _ in entries:
        fields = parse_fields(body)
        fmap = {name: (idx, indent, val) for idx, (indent, name, val) in enumerate(fields)}

        def scalar(name: str):
            if name not in fmap: return None
            v = fmap[name][2].strip()
            return v[1:-1] if v.startswith('"') and v.endswith('"') else None

        prefab_str  = scalar('prefab')
        display_str = scalar('displayName')

        dump_entry = by_prefab.get(prefab_str) if prefab_str else None
        if dump_entry is None and display_str:
            cands = by_displayname.get(display_str, [])
            if len(cands) == 1:
                dump_entry = cands[0]
        if dump_entry is None:
            no_match.append(key)
            chunks.append(f'  ["{key}"] = {{{body}}},\n')
            continue

        new_vals = overlay(key, dump_entry, faction_names, rank_names)

        # Skip the entire shop overlay for hulls flagged notForSale (story
        # rewards / drones / NPC-only ships). The prefab carries a populated
        # shopItemData but the ship never appears in any shipyard stock.
        not_for_sale = fmap.get('notForSale', (None, None, None))[2]
        if not_for_sale and not_for_sale.strip() == 'true':
            for skip in SHOP_FIELDS:
                new_vals.pop(skip, None)

        # Diff
        changes = []
        for name, new in new_vals.items():
            old_text = fmap.get(name, (None, None, None))[2]
            new_emit = emit_field_value(name, new)
            if old_text is None:
                changes.append((name, '<missing>', new_emit))
            elif old_text.strip() != new_emit.strip():
                changes.append((name, old_text.strip(), new_emit))

        if changes:
            n_changed += 1
            print(f"\n# {key}")
            for name, old, new in changes:
                print(f"  {name:14s} {old}  ->  {new}")
        else:
            n_unchanged += 1

        rendered = []
        written = set()
        for ind, name, val_text in fields:
            if name in new_vals:
                rendered.append(f'    {name:13s} = {emit_field_value(name, new_vals[name])},')
                written.add(name)
            else:
                rendered.append(f'    {name:13s} = {val_text},')
        for name, val in new_vals.items():
            if name in written: continue
            rendered.append(f'    {name:13s} = {emit_field_value(name, val)},')
        chunks.append(f'  ["{key}"] = {{\n' + '\n'.join(rendered) + '\n  },\n')

    chunks.append(trailer)
    args.output.write_text(''.join(chunks))

    print("\n--- summary ---", file=sys.stderr)
    print(f"changed:    {n_changed}", file=sys.stderr)
    print(f"unchanged:  {n_unchanged}", file=sys.stderr)
    if no_match:
        sample = ', '.join(no_match[:5])
        print(f"no match:   {len(no_match)} ({sample}{'...' if len(no_match) > 5 else ''})",
              file=sys.stderr)
    print(f"wrote {args.output}", file=sys.stderr)
    return 0


if __name__ == '__main__':
    sys.exit(main())
