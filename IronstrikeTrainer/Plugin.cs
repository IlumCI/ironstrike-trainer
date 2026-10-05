using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace IronstrikeTrainer;

internal static class Id
{
    public const string Guid    = "eu.euroswarms.ironstrike.trainer";
    public const string Name    = "Ironstrike Trainer";
    public const string Version = "0.1.1";
}

[BepInPlugin(Id.Guid, Id.Name, Id.Version)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static Cfg C;

    public override void Load()
    {
        Log = base.Log;
        C = new Cfg(Config);

        var h = new Harmony(Id.Guid);
        h.PatchAll(typeof(GmHooks));
        h.PatchAll(typeof(Cheats.Patches));
        h.PatchAll(typeof(OptionsProbe));
        h.PatchAll(typeof(SafeMode));

        Log.LogInfo($"{Id.Name} v{Id.Version} loaded. Waiting on GM.InitScene / GM.Update.");
    }

    // Every cheat goes through here. Fusion runs this game host-authoritative with almost no
    // validation -- ~36 of 45 RPCs are RpcSources.All and damage is a caller-supplied float --
    // so a modded client in someone else's lobby is their problem, not just ours.
    internal static bool Allowed()
    {
        if (!C.Enabled.Value) return false;
        return !C.SoloOnly.Value || GmHooks.IsSolo();
    }
}

internal sealed class Cfg
{
    public readonly ConfigEntry<bool> Enabled, SoloOnly, SafeMode;
    public readonly ConfigEntry<bool> HighDamage, FastRegen, LowCooldowns;
    public readonly ConfigEntry<bool> AllIronstrikes, DontSpawnIronstrikes;
    public readonly ConfigEntry<bool> NoHealthbars, NoDamageNumbers, NoStatusEffects;
    public readonly ConfigEntry<bool> UnlockDevMenu, EnableHotkeys;
    public readonly ConfigEntry<float> AutoOpenAfter;
    public readonly ConfigEntry<bool> WatchTamper, ProbeOptions;

    public Cfg(ConfigFile f)
    {
        Enabled  = f.Bind("01 General", "Enabled", true, "Master switch.");
        SoloOnly = f.Bind("01 General", "SoloOnly", true,
            "Only act when alone in the session. Leave this on.");
        SafeMode = f.Bind("01 General", "SafeMode", true,
            "Block public matchmaking entirely and refuse incoming connections while modded.\n"
            + "The developer asked that cheats never reach public games; this enforces it.\n"
            + "Turn this off only if you are certain you are offline.");

        HighDamage           = f.Bind("02 Cheats", "CheatHighDamage", false);
        FastRegen            = f.Bind("02 Cheats", "CheatFastRegen", false);
        LowCooldowns         = f.Bind("02 Cheats", "CheatLowCooldowns", false);
        AllIronstrikes       = f.Bind("02 Cheats", "CheatAllIronstrikes", false);
        DontSpawnIronstrikes = f.Bind("02 Cheats", "CheatDontSpawnIronstrikes", false);

        NoHealthbars    = f.Bind("03 Visual", "CheatNoHealthbars", false);
        NoDamageNumbers = f.Bind("03 Visual", "CheatNoDamageNumbers", false);
        NoStatusEffects = f.Bind("03 Visual", "CheatNoStatusEffecs", false,
            "Typo is the dev's; kept to match the field.");

        UnlockDevMenu = f.Bind("04 DevMenu", "UnlockDevMenu", true);
        ProbeOptions = f.Bind("05 Debug", "ProbeOptionsMenu", false,
            "Dump the options menu hierarchy and components the first time it opens.");
        WatchTamper = f.Bind("05 Debug", "WatchAntiTamper", false,
            "Log the game's anti-tamper state (GM.pleaseDontHack..., banbu, dcd) when it changes.");
        AutoOpenAfter = f.Bind("04 DevMenu", "AutoOpenAfterSeconds", 0f,
            "Debug aid: open the menu automatically this many seconds after a scene loads,\n"
            + "and log where it sits in the hierarchy. 0 disables.");
        EnableHotkeys = f.Bind("04 DevMenu", "EnableHotkeys", true,
            "Legacy Input only. Self-disables if this build is Input System only.");
    }
}
