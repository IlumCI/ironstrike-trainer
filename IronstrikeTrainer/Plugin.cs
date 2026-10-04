using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Assets.Scripts.Utilities;

namespace IronstrikeTrainer;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static Cfg C;

    public override void Load()
    {
        Log = base.Log;
        C = new Cfg(Config);

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} loading");

        // Phase 3 requirement: prove a hook actually fires before trusting any logic.
        // Under IL2CPP an inlined target applies cleanly and then silently never runs.
        try
        {
            var h = new Harmony(MyPluginInfo.PLUGIN_GUID);
            h.PatchAll(typeof(GmHooks));
            h.PatchAll(typeof(Cheats.Patches));
            Log.LogInfo("Harmony patches applied. Waiting for GM.InitScene / GM.Update to fire...");
        }
        catch (Exception e)
        {
            Log.LogError($"Harmony PatchAll failed: {e}");
        }
    }

    /// <summary>
    /// Single gate every cheat consults. IRONSTRIKE is Fusion Host-mode co-op with essentially no
    /// server-side validation, so a modded client in a shared lobby affects other people. The
    /// restraint has to live here because the game will not enforce it.
    /// </summary>
    internal static bool Allowed()
    {
        if (!C.Enabled.Value) return false;
        if (!C.SoloOnly.Value) return true;
        return GmHooks.IsSolo();
    }
}

/// <summary>Config, grouped so it reads sensibly in BepInEx's config file and in r2modman.</summary>
internal sealed class Cfg
{
    public readonly ConfigEntry<bool> Enabled;
    public readonly ConfigEntry<bool> SoloOnly;

    public readonly ConfigEntry<bool> HighDamage;
    public readonly ConfigEntry<bool> FastRegen;
    public readonly ConfigEntry<bool> LowCooldowns;
    public readonly ConfigEntry<bool> AllIronstrikes;
    public readonly ConfigEntry<bool> DontSpawnIronstrikes;
    public readonly ConfigEntry<bool> NoHealthbars;
    public readonly ConfigEntry<bool> NoDamageNumbers;
    public readonly ConfigEntry<bool> NoStatusEffects;

    public readonly ConfigEntry<bool> UnlockDevMenu;
    public readonly ConfigEntry<bool> EnableHotkeys;

    public Cfg(ConfigFile f)
    {
        Enabled = f.Bind("01 General", "Enabled", true,
            "Master switch. If false the trainer does nothing at all.");
        SoloOnly = f.Bind("01 General", "SoloOnly", true,
            "Only act when you are alone in the session (SpawnedPlayerCount <= 1).\n" +
            "IRONSTRIKE is Fusion Host-mode co-op with no server-side validation, so a modded\n" +
            "client in a shared lobby affects other people. Leave this on.");

        HighDamage = f.Bind("02 Cheats", "CheatHighDamage", false, "GM.CheatHighDamage");
        FastRegen = f.Bind("02 Cheats", "CheatFastRegen", false, "GM.CheatFastRegen");
        LowCooldowns = f.Bind("02 Cheats", "CheatLowCooldowns", false, "GM.CheatLowCooldowns");
        AllIronstrikes = f.Bind("02 Cheats", "CheatAllIronstrikes", false, "GM.CheatAllIronstrikes");
        DontSpawnIronstrikes = f.Bind("02 Cheats", "CheatDontSpawnIronstrikes", false,
            "GM.CheatDontSpawnIronstrikes");

        NoHealthbars = f.Bind("03 Visual", "CheatNoHealthbars", false, "GM.CheatNoHealthbars");
        NoDamageNumbers = f.Bind("03 Visual", "CheatNoDamageNumbers", false, "GM.CheatNoDamageNumbers");
        NoStatusEffects = f.Bind("03 Visual", "CheatNoStatusEffecs", false,
            "GM.CheatNoStatusEffecs (the typo is the dev's, kept to match the field)");

        UnlockDevMenu = f.Bind("04 DevMenu", "UnlockDevMenu", true,
            "Set GM.instance.AllowDebugMenu = true so the shipped dev menu can open.");
        EnableHotkeys = f.Bind("04 DevMenu", "EnableHotkeys", true,
            "Poll keyboard hotkeys each frame. Disable if the game uses Input System only\n" +
            "(the trainer detects this and self-disables anyway).");
    }
}
