using System.Collections.Generic;

namespace VGDataExport.Export;

internal sealed record ConquestRankNameRecord(
    string Rank,
    string Name);

internal sealed record FactionRecord(
    string  Identifier,
    string  Name,
    string? Description,
    IReadOnlyList<ConquestRankNameRecord> ConquestRanks);

internal sealed record HardpointRecord(
    int    Index,
    string Size,
    string? DefaultEquipment);

internal sealed record ModuleSlotRecord(
    int    Index,
    string Slot,    // EquipmentSlot enum name
    string Size);   // ModuleSize enum name

/// <summary>
/// One reputation/conquest gate the player must clear at a shipyard whose faction matches.
/// <see cref="Faction"/> is the faction id; null/empty means "the host shipyard's faction".
/// <see cref="ConquestRank"/> = "None" means no conquest gate.
/// </summary>
internal sealed record FactionPrerequisiteRecord(
    string? Faction,
    string  ReputationLevel,
    string  ConquestRank);

/// <summary>
/// Shipyard purchase requirements. Matches the in-game "lvl / rep / conquest" tooltip:
/// player level + reputation tier + conquest rank, gated against the shipyard's faction.
/// </summary>
internal sealed record ShopRequirementsRecord(
    int                                     MinAreaLevel,
    int                                     MaxAreaLevel,
    int                                     LevelRequirement,
    bool                                    PaysInConquestCommendations,
    IReadOnlyList<FactionPrerequisiteRecord> FactionPrereqs);

internal sealed record ShipRecord(
    string                       PrefabName,
    string                       DisplayName,
    string?                      Manufacturer,
    string?                      ClassName,
    int?                         ClassSize,
    string?                      Role,
    string?                      GameplayType,
    int                          HardpointCount,
    IReadOnlyList<HardpointRecord> Hardpoints,
    Dictionary<string, int>      SizeSummary,
    float                        EquivalentTurretsByConvention,
    int                          MaxOfficers,
    int                          MaxGrunts,
    int                          CargoCapacity,
    int                          Tonnage,
    float?                       MaxWarpSpeed,
    float?                       WarpAcceleration,
    float?                       ShipSellValue,
    bool                         HasCommander,
    IReadOnlyList<ModuleSlotRecord> ModuleSlots,
    ShopRequirementsRecord?      ShopRequirements,
    float                        BaseHullHP,
    float                        BaseShieldHP,
    float                        BaseArmorHP,
    float                        HullHPScale,
    float                        ShieldHPScale,
    float                        ArmorHPScale);

internal sealed record StatLineRecord(
    string Stat,
    float  Amount,
    float  Multiplier);

internal sealed record TurretRecord(
    string                    PrefabName,
    string                    DisplayName,
    string                    PowerStat,
    string                    TurretType,
    float                     TurretEquivalentRating,
    float                     PowerMultiplier,
    float                     DisplayedPower,
    int                       MaxMagSize,
    float                     FireDelay,
    float                     ReloadDelay,
    int                       BurstAmount,
    float                     BurstDelay,
    IReadOnlyList<StatLineRecord> IntrinsicStats);

/// <summary>
/// One rolled instance of a turret EquipmentBuilder at a given (level, rarity).
/// CombatPower / MiningPower / SalvagePower live in StatLines as the line whose
/// stat name matches the turret's powerStat.
/// </summary>
internal sealed record InstanceRecord(
    string Rarity,
    int    Level,
    float  Power,
    IReadOnlyList<StatLineRecord> StatLines);

internal sealed record BuilderInstanceRecord(
    string                       BuilderName,
    string                       BaseItemName,
    string                       PowerStat,
    string                       Size,
    float                        TurretEquivalentRating,
    IReadOnlyList<InstanceRecord> Instances);

internal sealed record EquippedTurretRecord(
    string                    PrefabName,
    string                    DisplayName,
    string                    PowerStat,
    string                    TurretType,
    float                     TurretEquivalentRating,
    float                     PowerMultiplier,
    float                     DisplayedPower,
    IReadOnlyList<StatLineRecord> StatLines);

internal sealed record PlayerSnapshot(
    string                            ShipPrefab,
    string                            ShipDisplayName,
    float                             CombatEquivalentTurrets,
    float                             MiningEquivalentTurrets,
    float                             SalvageEquivalentTurrets,
    float                             ShipCombatPower,
    float                             ShipMiningPower,
    float                             ShipSalvagePower,
    IReadOnlyList<EquippedTurretRecord> Turrets);

internal sealed record ArmorModuleRecord(
    string                    PrefabName,
    string                    DisplayName,
    float                     ResistAmount,
    float                     WeakAmount,
    IReadOnlyList<string>     WeakTypes,
    float                     OverrideBaseCapacity);

internal sealed record AspectRecord(
    string                    PrefabName,
    string                    Identifier,
    bool                      Common,
    IReadOnlyList<StatLineRecord> BoostStats);

internal sealed record DumpEnvelope(
    string                            Generated,
    string                            Plugin,
    string                            PluginVersion,
    int                               ShipCount,
    int                               TurretCount,
    IReadOnlyList<ShipRecord>         Ships,
    IReadOnlyList<TurretRecord>       Turrets,
    IReadOnlyList<BuilderInstanceRecord> ReferenceInstances,
    IReadOnlyList<ArmorModuleRecord>  ArmorModules,
    IReadOnlyList<AspectRecord>       Aspects,
    IReadOnlyList<FactionRecord>      Factions,
    PlayerSnapshot?                   Player);
