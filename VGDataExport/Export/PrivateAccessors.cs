using Behaviour.Equipment.Turret;
using Behaviour.Unit;
using HarmonyLib;
using Source.Data;

namespace VGDataExport.Export;

/// <summary>
/// FieldRefAccess delegates for private fields. The publicized Assembly-CSharp.dll
/// stub lets us see them at compile time, but the game's actual DLL keeps them
/// private — direct ldfld/stfld throws FieldAccessException at JIT, mirroring
/// the same workaround used in VGHardpointDPS.
/// </summary>
internal static class PrivateAccessors
{
    // ---- Turret tuning ----
    public static readonly AccessTools.FieldRef<AbstractTurret, float> FireDelay =
        AccessTools.FieldRefAccess<AbstractTurret, float>("_fireDelay");

    public static readonly AccessTools.FieldRef<AbstractTurret, float> ReloadDelay =
        AccessTools.FieldRefAccess<AbstractTurret, float>("_reloadDelay");

    public static readonly AccessTools.FieldRef<AbstractTurret, int> MaxMagSize =
        AccessTools.FieldRefAccess<AbstractTurret, int>("_maxMagSize");

    // ---- SpaceShip prefab fields ----
    public static readonly AccessTools.FieldRef<SpaceShip, float> BaseMaxWarpSpeed =
        AccessTools.FieldRefAccess<SpaceShip, float>("baseMaxWarpSpeed");

    public static readonly AccessTools.FieldRef<SpaceShip, float> BaseWarpAcceleration =
        AccessTools.FieldRefAccess<SpaceShip, float>("baseWarpAcceleration");

    // ---- ShopItemData numeric fields ----
    public static readonly AccessTools.FieldRef<ShopItemData, int> MinAreaLevel =
        AccessTools.FieldRefAccess<ShopItemData, int>("minAreaLevelRequirement");

    public static readonly AccessTools.FieldRef<ShopItemData, int> MaxAreaLevel =
        AccessTools.FieldRefAccess<ShopItemData, int>("maxAreaLevelRequirement");

    public static readonly AccessTools.FieldRef<ShopItemData, int> LevelRequirement =
        AccessTools.FieldRefAccess<ShopItemData, int>("levelRequirement");

    public static readonly AccessTools.FieldRef<ShopItemData, bool> ConquestCommendations =
        AccessTools.FieldRefAccess<ShopItemData, bool>("conquestCommendations");
}
