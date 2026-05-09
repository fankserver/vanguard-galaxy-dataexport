using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Behaviour.Equipment;
using Behaviour.Equipment.Aspect;
using Behaviour.Equipment.Builder;
using Behaviour.Equipment.Module;
using Behaviour.Equipment.Turret;
using Behaviour.Item;
using Behaviour.Unit;
using BepInEx.Logging;
using Source.Data;
using Source.Galaxy;
using Newtonsoft.Json;
using Source.Item;
using Source.SpaceShip;
using Source.Util;
using UnityEngine;

namespace VGDataExport.Export;

internal static class ExportRunner
{
    /// <summary>
    /// Wrap a pass so an exception in one stage (e.g. mid-load NRE in
    /// EquipmentBuilder.availableBuilders) doesn't kill the rest of the
    /// dump. Returns default(T) on failure after logging.
    /// </summary>
    private static T? SafeRun<T>(Func<T> fn, string label, ManualLogSource log) where T : class
    {
        try { return fn(); }
        catch (Exception e)
        {
            log.LogWarning($"{label} pass failed ({e.GetType().Name}: {e.Message}); continuing.");
            return null;
        }
    }

    private static readonly Dictionary<ModuleSize, float> SizeRating = new()
    {
        [ModuleSize.Tiny]   = 0.45f,
        [ModuleSize.Small]  = 1.00f,
        [ModuleSize.Medium] = 2.00f,
        [ModuleSize.Large]  = 3.00f,
    };

    public static void DumpAll(
        string                 outputDir,
        IReadOnlyList<int>     referenceLevels,
        IReadOnlyList<Rarity>  referenceRarities,
        ManualLogSource        log)
    {
        Directory.CreateDirectory(outputDir);

        var ships     = SafeRun(() => CollectShips(log),                                          "ships",              log) ?? new List<ShipRecord>();
        var turrets   = SafeRun(() => CollectTurrets(log),                                        "turrets",            log) ?? new List<TurretRecord>();
        var refRolls  = SafeRun(() => CollectReferenceInstances(referenceLevels, referenceRarities, log), "reference instances", log) ?? new List<BuilderInstanceRecord>();
        var armor     = SafeRun(() => CollectArmorModules(log),                                   "armor modules",      log) ?? new List<ArmorModuleRecord>();
        var aspects   = SafeRun(() => CollectAspects(log),                                        "aspects",            log) ?? new List<AspectRecord>();
        var factions  = SafeRun(() => CollectFactions(log),                                       "factions",           log) ?? new List<FactionRecord>();
        PlayerSnapshot? snapshot = null;
        try { snapshot = CollectPlayerSnapshot(log); }
        catch (Exception e) { log.LogWarning($"player snapshot pass failed ({e.GetType().Name}: {e.Message}); continuing."); }

        var envelope = new DumpEnvelope(
            Generated:           DateTime.UtcNow.ToString("o"),
            Plugin:              Plugin.PluginName,
            PluginVersion:       Plugin.PluginVersion,
            ShipCount:           ships.Count,
            TurretCount:         turrets.Count,
            Ships:               ships,
            Turrets:             turrets,
            ReferenceInstances:  refRolls,
            ArmorModules:        armor,
            Aspects:             aspects,
            Factions:            factions,
            Player:              snapshot);

        var json = JsonConvert.SerializeObject(envelope, Formatting.Indented);
        File.WriteAllText(Path.Combine(outputDir, "vg-data.json"), json);
        log.LogInfo($"Wrote {ships.Count} ships, {turrets.Count} turrets, " +
                    $"{refRolls.Count} reference builders, {armor.Count} armor modules, " +
                    $"{aspects.Count} aspects, {factions.Count} factions, " +
                    $"{(snapshot?.Turrets.Count ?? 0)} player turret(s) to vg-data.json");

        File.WriteAllText(Path.Combine(outputDir, "ships.json"),
            JsonConvert.SerializeObject(ships, Formatting.Indented));
        File.WriteAllText(Path.Combine(outputDir, "turrets.json"),
            JsonConvert.SerializeObject(turrets, Formatting.Indented));
        File.WriteAllText(Path.Combine(outputDir, "reference-instances.json"),
            JsonConvert.SerializeObject(refRolls, Formatting.Indented));
        File.WriteAllText(Path.Combine(outputDir, "armor-modules.json"),
            JsonConvert.SerializeObject(armor, Formatting.Indented));
        File.WriteAllText(Path.Combine(outputDir, "aspects.json"),
            JsonConvert.SerializeObject(aspects, Formatting.Indented));
        File.WriteAllText(Path.Combine(outputDir, "factions.json"),
            JsonConvert.SerializeObject(factions, Formatting.Indented));
        if (snapshot != null)
            File.WriteAllText(Path.Combine(outputDir, "player.json"),
                JsonConvert.SerializeObject(snapshot, Formatting.Indented));
    }

    /// <summary>
    /// Walks all loaded ArmorModule prefabs and dumps their innate
    /// resistance tuning. The wiki Damage page wants these to quantify
    /// armor-tank vs shield-tank effective HP.
    /// </summary>
    private static List<ArmorModuleRecord> CollectArmorModules(ManualLogSource log)
    {
        var seen = new HashSet<string>();
        var output = new List<ArmorModuleRecord>();

        foreach (var m in Resources.FindObjectsOfTypeAll<ArmorModule>())
        {
            if (m == null) continue;
            var key = m.gameObject != null ? m.gameObject.name : m.name;
            if (!seen.Add(key)) continue;

            var weakTypes = new List<string>();
            if (m.weakTypes != null)
                foreach (var t in m.weakTypes)
                    weakTypes.Add(t.ToString());

            output.Add(new ArmorModuleRecord(
                PrefabName:            key,
                DisplayName:           string.IsNullOrEmpty(m.typeDisplayName) ? key : m.typeDisplayName,
                ResistAmount:          m.resistAmount,
                WeakAmount:            m.weakAmount,
                WeakTypes:             weakTypes,
                OverrideBaseCapacity:  m.overrideBaseCapacity));
        }

        output.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        log.LogInfo($"Armor modules: {output.Count} prefabs.");
        return output;
    }

    /// <summary>
    /// Walks all loaded EquipAspect prefabs and dumps their BoostStat children's
    /// stat lines. Aspects are attached to equipment slots (turrets, modules,
    /// armor, etc.) and modify the parent equipment's stats — for example
    /// "ArmorResistance" adding +5% DamageReduction per stack.
    /// </summary>
    private static List<AspectRecord> CollectAspects(ManualLogSource log)
    {
        var seen = new HashSet<string>();
        var output = new List<AspectRecord>();

        foreach (var a in Resources.FindObjectsOfTypeAll<EquipAspect>())
        {
            if (a == null) continue;
            var key = a.gameObject != null ? a.gameObject.name : a.name;
            // Strip clone suffixes so e.g. "ArmorResistance(Clone)(Clone)" matches the base prefab.
            var clean = key.Replace("(Clone)", "");
            if (!seen.Add(clean)) continue;

            var lines = new List<StatLineRecord>();
            try
            {
                foreach (var bs in a.GetComponents<BoostStat>())
                {
                    foreach (var line in bs.GetStats())
                        lines.Add(new StatLineRecord(line.stat.ToString(), line.amount, line.multiplier));
                }
            }
            catch (Exception e)
            {
                log.LogWarning($"BoostStat read failed on aspect {clean}: {e.GetType().Name}");
            }

            output.Add(new AspectRecord(
                PrefabName:  clean,
                Identifier:  a.identifier,
                Common:      a.common,
                BoostStats:  lines));
        }

        output.Sort((x, y) => string.Compare(x.PrefabName, y.PrefabName, StringComparison.Ordinal));
        log.LogInfo($"Aspects: {output.Count} prefabs.");
        return output;
    }

    /// <summary>
    /// Walks Faction.allFactions and dumps identifier + display name + description.
    /// Used wiki-side to translate <c>shipyardFaction</c> raw enum ids ("Blue",
    /// "Marauders", "Darkspacers", ...) into the human-readable names that wiki
    /// pages already link to ("Stellar Industries", "Corsair Syndicate", ...).
    /// </summary>
    private static List<FactionRecord> CollectFactions(ManualLogSource log)
    {
        var output = new List<FactionRecord>();
        IEnumerable<Faction> all;
        try { all = Faction.all; }
        catch (Exception e)
        {
            log.LogWarning($"Faction.all enumeration failed ({e.GetType().Name}: {e.Message}); skipping.");
            return output;
        }
        foreach (var f in all)
        {
            if (f == null) continue;
            string id = "?";
            string name = "?";
            string? desc = null;
            try { id   = f.identifier; }   catch { /* tolerate */ }
            try { name = f.name;       }   catch { /* tolerate */ }
            try { desc = f.description; }  catch { /* tolerate */ }
            if (string.IsNullOrEmpty(id)) continue;
            // The .name and .description getters return raw "@FactionNameBlue"-style
            // translation keys. Run them through Translation.Translate so consumers
            // get the human-readable strings the game UI shows.
            try { if (!string.IsNullOrEmpty(name)) name = Translation.Translate(name); } catch { /* tolerate */ }
            try { if (!string.IsNullOrEmpty(desc)) desc = Translation.Translate(desc); } catch { /* tolerate */ }

            // Per-faction conquest rank display names. Key format is
            // `@{factionId}{rank}` per ConquestRankExtension.GetConquestRankTranslation.
            // E.g. Stellar Industries Rank2 -> "Associate", Marauders Rank2 -> "Cutthroat".
            var ranks = new List<ConquestRankNameRecord>();
            foreach (var rank in (ConquestRank[])Enum.GetValues(typeof(ConquestRank)))
            {
                if (rank == ConquestRank.None) continue;
                string rankName;
                try { rankName = rank.GetConquestRankTranslation(id); }
                catch { continue; }
                if (string.IsNullOrEmpty(rankName) || rankName.StartsWith("@")) continue;
                ranks.Add(new ConquestRankNameRecord(rank.ToString(), rankName));
            }

            output.Add(new FactionRecord(id, name ?? id, string.IsNullOrEmpty(desc) ? null : desc, ranks));
        }
        output.Sort((a, b) => string.Compare(a.Identifier, b.Identifier, StringComparison.Ordinal));
        log.LogInfo($"Factions: {output.Count} entries.");
        return output;
    }

    // ---- Pass 1: ship hardpoint catalog ------------------------------------

    private static List<ShipRecord> CollectShips(ManualLogSource log)
    {
        var seen = new HashSet<string>();
        var output = new List<ShipRecord>();

        foreach (var unit in Resources.FindObjectsOfTypeAll<AbstractUnit>())
        {
            if (unit == null || unit.hardpointSlots == null || unit.hardpointSlots.Length == 0) continue;
            var key = unit.gameObject != null ? unit.gameObject.name : unit.name;
            if (!seen.Add(key)) continue;

            try
            {

            var hardpoints = new List<HardpointRecord>(unit.hardpointSlots.Length);
            float weightSum = 0f;
            var sizeSummary = new Dictionary<string, int>();

            for (int i = 0; i < unit.hardpointSlots.Length; i++)
            {
                var hp = unit.hardpointSlots[i];
                if (hp == null) continue;
                var sizeName = hp.size.ToString();
                sizeSummary[sizeName] = sizeSummary.TryGetValue(sizeName, out var c) ? c + 1 : 1;
                weightSum += SizeRating.TryGetValue(hp.size, out var w) ? w : 0f;
                hardpoints.Add(new HardpointRecord(
                    Index:            i,
                    Size:             sizeName,
                    DefaultEquipment: hp.defaultEquipment != null ? hp.defaultEquipment.gameObject.name : null));
            }

            // Aux module slots (Reactor / Engine / DroneBay / MiningSystem / etc.)
            var moduleSlots = new List<ModuleSlotRecord>();
            if (unit.moduleSlots != null)
            {
                for (int i = 0; i < unit.moduleSlots.Length; i++)
                {
                    var m = unit.moduleSlots[i];
                    if (m == null) continue;
                    moduleSlots.Add(new ModuleSlotRecord(i, m.slot.ToString(), m.size.ToString()));
                }
            }

            // SpaceShip-only fields (warp speeds, role/class, sell value, crew slots).
            // AbstractUnit subclasses that aren't SpaceShip leave these null/0.
            string? manufacturerName = null;
            try { manufacturerName = unit.manufacturer.GetDisplayName(); }
            catch (Exception e) { log.LogWarning($"manufacturer name failed for {key}: {e.GetType().Name}"); }

            string? className     = null;
            int?    classSize     = null;
            string? role          = null;
            string? gameplayType  = null;
            int     maxOfficers  = 0;
            int     maxGrunts    = 0;
            float?  maxWarpSpeed     = null;
            float?  warpAcceleration = null;
            float?  shipSellValue    = null;
            bool    hasCommander     = false;

            ShopRequirementsRecord? shopReqs = null;

            if (unit is SpaceShip ship)
            {
                try
                {
                    var rt = ship.shipRoleType;
                    if (rt != null)
                    {
                        className    = rt.GetTypeName();
                        classSize    = rt.GetTypeSize();
                        // Use the accessor — the shipped DLL has `role` as a private field
                        // even though the publicized stub exposes it as public.
                        role         = rt.GetRole().ToString();
                        gameplayType = rt.GetGameplayType().ToString();
                    }
                }
                catch (Exception e) { log.LogWarning($"shipRoleType read failed for {key}: {e.GetType().Name}"); }

                // maxOfficers / maxGrunts are the slot-count properties (formerly maxCrew).
                // ship.crewMembers is the runtime-populated list and stays empty on prefabs.
                try { maxOfficers = ship.maxOfficers; } catch { /* tolerate */ }
                try { maxGrunts   = ship.maxGrunts;   } catch { /* tolerate */ }

                try { maxWarpSpeed     = PrivateAccessors.BaseMaxWarpSpeed(ship); }     catch { /* tolerate */ }
                try { warpAcceleration = PrivateAccessors.BaseWarpAcceleration(ship); } catch { /* tolerate */ }

                try { shipSellValue = ship.shipSellValue; }
                catch { /* tolerate */ }

                // hasCommander === "is this ship sold by any shipyard". Used by the
                // game's AddGeneralShips filter; quest/storyline rewards have it false.
                try { hasCommander = ship.hasCommander; }
                catch { /* tolerate */ }

                // Shipyard requirements: lvl / rep / conquest. Each ship advertises one
                // ShopItemData; the faction prereq list may be empty (= sold anywhere).
                try
                {
                    var sid = ship.shopItemData;
                    if (sid != null)
                    {
                        var prereqs = new List<FactionPrerequisiteRecord>();
                        if (sid.factionPrereq != null)
                        {
                            foreach (var fp in sid.factionPrereq)
                            {
                                if (fp == null) continue;
                                prereqs.Add(new FactionPrerequisiteRecord(
                                    Faction:         string.IsNullOrEmpty(fp.faction) ? null : fp.faction,
                                    ReputationLevel: fp.reputationLevel.ToString(),
                                    ConquestRank:    fp.conquestRank.ToString()));
                            }
                        }
                        shopReqs = new ShopRequirementsRecord(
                            MinAreaLevel:                PrivateAccessors.MinAreaLevel(sid),
                            MaxAreaLevel:                PrivateAccessors.MaxAreaLevel(sid),
                            LevelRequirement:            PrivateAccessors.LevelRequirement(sid),
                            PaysInConquestCommendations: PrivateAccessors.ConquestCommendations(sid),
                            FactionPrereqs:              prereqs);
                    }
                }
                catch (Exception e) { log.LogWarning($"shopItemData read failed for {key}: {e.GetType().Name}"); }
            }

            // _cargoCapacity is the raw base value; cargoCapacity is virtual and returns
            // a 10M sentinel for non-player faction ships, so it's useless on prefabs.
            int   cargo = 0; try { cargo = unit._cargoCapacity; } catch { /* tolerate */ }
            int   tons  = 0; try { tons  = unit.tonnage;        } catch { /* tolerate */ }

            output.Add(new ShipRecord(
                PrefabName:                    key,
                DisplayName:                   string.IsNullOrEmpty(unit.displayName) ? key : unit.displayName,
                Manufacturer:                  manufacturerName,
                ClassName:                     className,
                ClassSize:                     classSize,
                Role:                          role,
                GameplayType:                  gameplayType,
                HardpointCount:                hardpoints.Count,
                Hardpoints:                    hardpoints,
                SizeSummary:                   sizeSummary,
                EquivalentTurretsByConvention: weightSum,
                MaxOfficers:                   maxOfficers,
                MaxGrunts:                     maxGrunts,
                CargoCapacity:                 cargo,
                Tonnage:                       tons,
                MaxWarpSpeed:                  maxWarpSpeed,
                WarpAcceleration:              warpAcceleration,
                ShipSellValue:                 shipSellValue,
                HasCommander:                  hasCommander,
                ModuleSlots:                   moduleSlots,
                ShopRequirements:              shopReqs,
                BaseHullHP:                    unit.baseHullHP,
                BaseShieldHP:                  unit.baseShieldHP,
                BaseArmorHP:                   unit.baseArmorHP,
                HullHPScale:                   unit.hullHPScale,
                ShieldHPScale:                 unit.shieldHPScale,
                ArmorHPScale:                  unit.armorHPScale));
            }
            catch (Exception e)
            {
                log.LogWarning($"Skipping ship {key}: {e.GetType().Name}: {e.Message}");
            }
        }

        output.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        return output;
    }

    // ---- Pass 2: turret prefab firing mechanics ----------------------------

    private static List<TurretRecord> CollectTurrets(ManualLogSource log)
    {
        var seen = new HashSet<string>();
        var output = new List<TurretRecord>();

        foreach (var t in Resources.FindObjectsOfTypeAll<AbstractTurret>())
        {
            if (t == null) continue;
            var key = t.gameObject != null ? t.gameObject.name : t.name;
            if (!seen.Add(key)) continue;

            int   maxMag, burstAmt;
            float fireDelay, reloadDelay, burstDelay;
            try
            {
                maxMag      = PrivateAccessors.MaxMagSize(t);
                fireDelay   = PrivateAccessors.FireDelay(t);
                reloadDelay = PrivateAccessors.ReloadDelay(t);
                burstAmt    = t.burstAmount;
                burstDelay  = t.burstDelay;
            }
            catch (Exception e)
            {
                log.LogWarning($"Skipping {key}: cannot read private fields ({e.GetType().Name})");
                continue;
            }

            output.Add(new TurretRecord(
                PrefabName:             key,
                DisplayName:            string.IsNullOrEmpty(t.typeDisplayName) ? key : t.typeDisplayName,
                PowerStat:              t.powerStat.ToString(),
                TurretType:             t.GetType().Name,
                TurretEquivalentRating: t.turretEquivalentRating,
                PowerMultiplier:        t.powerMultiplier,
                DisplayedPower:         t.displayedPower,
                MaxMagSize:             maxMag,
                FireDelay:              fireDelay,
                ReloadDelay:            reloadDelay,
                BurstAmount:            burstAmt,
                BurstDelay:             burstDelay,
                IntrinsicStats:         ReadStats(t)));
        }

        output.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        return output;
    }

    // ---- Pass 3: rolled reference instances --------------------------------

    /// <summary>
    /// For every turret-producing EquipmentBuilder, roll one instance per
    /// (level, rarity) combination. Captures the rolled CombatPower /
    /// MiningPower / SalvagePower and intrinsic stat lines that don't exist
    /// on the bare prefab. Uses a fixed seed per (builder, level, rarity) so
    /// runs are reproducible.
    /// </summary>
    private static List<BuilderInstanceRecord> CollectReferenceInstances(
        IReadOnlyList<int> levels, IReadOnlyList<Rarity> rarities, ManualLogSource log)
    {
        var output = new List<BuilderInstanceRecord>();
        if (levels.Count == 0 || rarities.Count == 0)
        {
            log.LogInfo("Reference-instance pass disabled (no levels or rarities configured).");
            return output;
        }

        EquipmentBuilder[] builders;
        try
        {
            // availableBuilders is a player-aware filter; mid-load the player
            // can be null and the underlying Linq throws NRE during enumeration.
            builders = EquipmentBuilder.availableBuilders.ToArray();
        }
        catch (Exception e)
        {
            log.LogWarning($"availableBuilders enumeration failed ({e.GetType().Name}: {e.Message}); " +
                           "skipping reference-instance pass this run. Re-run after a save loads.");
            return output;
        }

        int rolled = 0, skipped = 0;
        foreach (var builder in builders)
        {
            if (builder == null || builder.prefab == null) continue;

            // Filter to turret-bearing builders. We can't read displayedPower
            // from the prefab, but the prefab GameObject does carry the
            // AbstractTurret component, so we can identify turrets that way.
            var prefabTurret = builder.prefab.GetComponent<AbstractTurret>();
            if (prefabTurret == null) { skipped++; continue; }

            var instances = new List<InstanceRecord>(levels.Count * rarities.Count);
            foreach (var level in levels)
            foreach (var rarity in rarities)
            {
                InventoryItemType? item = null;
                try
                {
                    var seed = $"vgdataexport:{builder.identifier}:{level}:{rarity}";
                    item = builder.CreateItemType(rarity, level, exactLevel: true, seed: seed, ignoreLevelCap: true);
                    if (item == null) continue;
                    var equipment = item.GetComponent<AbstractEquipment>();
                    var turret    = equipment as AbstractTurret;
                    if (turret == null) continue;

                    var power = turret.displayedPower;
                    var lines = new List<StatLineRecord>();
                    foreach (var line in equipment.stats)
                    {
                        // Skip the powerStat line itself — already exposed as Power.
                        if (line.stat == turret.powerStat) continue;
                        lines.Add(new StatLineRecord(line.stat.ToString(), line.amount, line.multiplier));
                    }
                    instances.Add(new InstanceRecord(rarity.ToString(), level, power, lines));
                    rolled++;
                }
                catch (Exception e)
                {
                    log.LogWarning($"Failed to roll {builder.identifier} L{level} {rarity}: {e.GetType().Name}: {e.Message}");
                }
                finally
                {
                    if (item != null && item.gameObject != null)
                    {
                        try { UnityEngine.Object.Destroy(item.gameObject); }
                        catch { /* tolerate cleanup race */ }
                    }
                }
            }

            output.Add(new BuilderInstanceRecord(
                BuilderName:            builder.identifier,
                BaseItemName:           builder.prefab.gameObject.name,
                PowerStat:              prefabTurret.powerStat.ToString(),
                Size:                   prefabTurret.size.ToString(),
                TurretEquivalentRating: prefabTurret.turretEquivalentRating,
                Instances:              instances));
        }

        log.LogInfo($"Reference instances: rolled {rolled}, skipped {skipped} non-turret builders.");
        output.Sort((a, b) => string.Compare(a.BuilderName, b.BuilderName, StringComparison.Ordinal));
        return output;
    }

    // ---- Pass 4: player ship snapshot --------------------------------------

    /// <summary>
    /// Walks the player's currently active ship and dumps the actual rolled
    /// values on each equipped turret. Returns null if no player ship exists
    /// yet (e.g. main menu).
    /// </summary>
    private static PlayerSnapshot? CollectPlayerSnapshot(ManualLogSource log)
    {
        var mgr = GameplayManager.Instance;
        var ship = mgr != null ? mgr.spaceShip : null;
        if (ship == null)
        {
            log.LogInfo("Player snapshot skipped: no active ship (main menu / loading?).");
            return null;
        }

        var turrets = new List<EquippedTurretRecord>();
        foreach (var t in ship.GetComponentsInChildren<AbstractTurret>())
        {
            if (t == null) continue;
            turrets.Add(new EquippedTurretRecord(
                PrefabName:             t.gameObject != null ? t.gameObject.name : t.name,
                DisplayName:            string.IsNullOrEmpty(t.typeDisplayName) ? t.name : t.typeDisplayName,
                PowerStat:              t.powerStat.ToString(),
                TurretType:             t.GetType().Name,
                TurretEquivalentRating: t.turretEquivalentRating,
                PowerMultiplier:        t.powerMultiplier,
                DisplayedPower:         t.displayedPower,
                StatLines:              ReadStats(t)));
        }

        return new PlayerSnapshot(
            ShipPrefab:               ship.gameObject != null ? ship.gameObject.name : ship.name,
            ShipDisplayName:          string.IsNullOrEmpty(ship.displayName) ? ship.name : ship.displayName,
            CombatEquivalentTurrets:  ship.GetEquivalentTurretsCount(EquipStat.CombatPower),
            MiningEquivalentTurrets:  ship.GetEquivalentTurretsCount(EquipStat.MiningPower),
            SalvageEquivalentTurrets: ship.GetEquivalentTurretsCount(EquipStat.SalvagePower),
            ShipCombatPower:          ship.GetStat(EquipStat.CombatPower),
            ShipMiningPower:          ship.GetStat(EquipStat.MiningPower),
            ShipSalvagePower:         ship.GetStat(EquipStat.SalvagePower),
            Turrets:                  turrets);
    }

    private static List<StatLineRecord> ReadStats(AbstractTurret t)
    {
        var stats = new List<StatLineRecord>();
        try
        {
            foreach (EquipStatLine line in t.GetStats())
                stats.Add(new StatLineRecord(line.stat.ToString(), line.amount, line.multiplier));
        }
        catch { /* tolerate */ }
        return stats;
    }
}
