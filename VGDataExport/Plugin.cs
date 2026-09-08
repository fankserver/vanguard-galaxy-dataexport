using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Source.Item;
using UnityEngine;
using VGDataExport.Export;

namespace VGDataExport;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid    = "vgdataexport";
    public const string PluginName    = "Data Export";
    public const string PluginVersion = "0.2.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private ConfigEntry<bool>    _autoExport       = null!;
    private ConfigEntry<float>   _autoDelaySeconds = null!;
    private ConfigEntry<string>  _outputDir        = null!;
    private ConfigEntry<KeyCode> _hotkeyPrimary    = null!;
    private ConfigEntry<string>  _refLevels        = null!;
    private ConfigEntry<string>  _refRarities      = null!;

    private void Awake()
    {
        Log = Logger;

        _autoExport       = Config.Bind("Export", "AutoExport", true,
            "Run a single dump once a savegame is loaded (waits for the player ship to exist).");
        _autoDelaySeconds = Config.Bind("Export", "AutoDelaySeconds", 5f,
            "Extra seconds to wait after the player ship appears so prefabs have time to settle.");
        _outputDir        = Config.Bind("Export", "OutputDir", "BepInEx/dataexport",
            "Output directory, relative to the game install root.");
        _hotkeyPrimary    = Config.Bind("Export", "Hotkey", KeyCode.F10,
            "Press Ctrl+Shift+<this key> to trigger an export at any time.");
        _refLevels        = Config.Bind("Reference", "Levels", "10,20,30",
            "Comma-separated item levels to roll reference instances at.");
        _refRarities      = Config.Bind("Reference", "Rarities", "Standard,Enhanced,HighGrade",
            "Comma-separated rarities to roll reference instances at. " +
            "Valid: Standard, Enhanced, HighGrade, Exotic, Legendary.");

        if (_autoExport.Value)
            StartCoroutine(AutoExportCoroutine());

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded. Hotkey: Ctrl+Shift+{_hotkeyPrimary.Value}.");
    }

    private IEnumerator AutoExportCoroutine()
    {
        // Wait for a savegame to load. Many prefabs and game-state singletons
        // are uninitialized at the main menu (NREs in shopItemData reads,
        // empty availableBuilders, etc.), so dumping there produces an empty
        // file and noisy warnings. GameplayManager.spaceShip becoming non-null
        // is the cheapest reliable "we're in-game" signal.
        var wait = new WaitForSeconds(1f);
        while (true)
        {
            try
            {
                if (GameplayManager.Instance != null && GameplayManager.Instance.spaceShip != null)
                    break;
            }
            catch { /* tolerate races during scene transition */ }
            yield return wait;
        }

        yield return new WaitForSeconds(_autoDelaySeconds.Value);
        TryExport(reason: "auto");
    }

    private void Update()
    {
        if (!Input.GetKeyDown(_hotkeyPrimary.Value)) return;
        if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
        if (!Input.GetKey(KeyCode.LeftShift)   && !Input.GetKey(KeyCode.RightShift))   return;
        TryExport(reason: "hotkey");
    }

    private void TryExport(string reason)
    {
        try
        {
            var dir = Path.IsPathRooted(_outputDir.Value)
                ? _outputDir.Value
                : Path.Combine(Paths.GameRootPath, _outputDir.Value);

            var levels   = ParseLevels(_refLevels.Value);
            var rarities = ParseRarities(_refRarities.Value);

            Log.LogInfo($"Exporting catalog ({reason}) -> {dir}  " +
                        $"refLevels=[{string.Join(",", levels)}]  " +
                        $"refRarities=[{string.Join(",", rarities)}]");
            ExportRunner.DumpAll(dir, levels, rarities, Log);
        }
        catch (Exception e)
        {
            Log.LogError($"Export failed ({reason}): {e}");
        }
    }

    private static List<int> ParseLevels(string csv)
    {
        var result = new List<int>();
        foreach (var part in csv.Split(','))
        {
            if (int.TryParse(part.Trim(), out var n) && n > 0) result.Add(n);
        }
        return result;
    }

    private static List<Rarity> ParseRarities(string csv)
    {
        var result = new List<Rarity>();
        foreach (var part in csv.Split(','))
        {
            if (Enum.TryParse<Rarity>(part.Trim(), ignoreCase: true, out var r)) result.Add(r);
        }
        return result;
    }
}
