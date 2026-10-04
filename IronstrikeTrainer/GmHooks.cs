using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Assets.Scripts.Utilities;
using UnityEngine;

namespace IronstrikeTrainer;

// GM.InitScene and GM.Update are Unity messages called from native code, so IL2CPP can't inline
// them away. That makes them the safe hook points here.
[HarmonyPatch]
internal static class GmHooks
{
    static bool sawInit, sawUpdate, inputDead, menuGrafted;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GM), nameof(GM.InitScene))]
    static void InitScene()
    {
        if (!sawInit) { sawInit = true; Plugin.Log.LogInfo("HOOK CONFIRMED: GM.InitScene fired."); }
        menuGrafted = false;   // dev menu is rebuilt per scene
        Apply("InitScene");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GM), nameof(GM.Update))]
    static void Update()
    {
        if (!sawUpdate) { sawUpdate = true; Plugin.Log.LogInfo("HOOK CONFIRMED: GM.Update fired."); }
        if (!Plugin.C.Enabled.Value) return;

        // Keep this cheap. VR runs 72-120Hz and allocating here shows up as judder.
        Cheats.Patches.PinGodMode();
        if (Plugin.C.EnableHotkeys.Value && !inputDead) Hotkeys();
    }

    // NetworkIsRunning() is not a solo test: solo play still starts a Fusion Host session, since
    // GM.LoadAsyncSceneByIndex passes GameMode.Host. Player count is the real check.
    internal static bool IsSolo()
    {
        try
        {
            var nl = GM.instance?.NetLifecycle;
            if (nl == null) return true;
            return !nl.NetworkIsRunning() || nl.SpawnedPlayerCount <= 1;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"solo check failed, assuming not solo: {e.Message}");
            return false;
        }
    }

    internal static void Apply(string why)
    {
        if (!Plugin.C.Enabled.Value) return;
        if (!Plugin.Allowed()) { Plugin.Log.LogInfo($"skipped {why}: not solo"); return; }

        var c = Plugin.C;
        GM.CheatHighDamage           = c.HighDamage.Value;
        GM.CheatFastRegen            = c.FastRegen.Value;
        GM.CheatLowCooldowns         = c.LowCooldowns.Value;
        GM.CheatAllIronstrikes       = c.AllIronstrikes.Value;
        GM.CheatDontSpawnIronstrikes = c.DontSpawnIronstrikes.Value;
        GM.CheatNoHealthbars         = c.NoHealthbars.Value;
        GM.CheatNoDamageNumbers      = c.NoDamageNumbers.Value;
        GM.CheatNoStatusEffecs       = c.NoStatusEffects.Value;

        var gm = GM.instance;
        if (gm != null && c.UnlockDevMenu.Value) gm.AllowDebugMenu = true;

        Cheats.ApplyWeaponTweaks();
        Plugin.Log.LogInfo($"applied ({why})");
    }

    static void Hotkeys()
    {
        try
        {
            if (Down(KeyCode.F1)) ToggleDevMenu();
            else if (Down(KeyCode.F2)) Apply("F2");
            else if (Down(KeyCode.F3)) Revive();
            else if (Down(KeyCode.F4)) Bots(true);
            else if (Down(KeyCode.F5)) Bots(false);
        }
        catch (Exception e)
        {
            inputDead = true;
            Plugin.Log.LogWarning(
                $"hotkeys off ({e.GetType().Name}). Everything is in the VR dev menu anyway.");
        }
    }

    static bool Down(KeyCode k) => Input.GetKeyDown(k);

    internal static void ToggleDevMenu()
    {
        if (!Plugin.Allowed()) { Plugin.Log.LogInfo("dev menu blocked: not solo"); return; }

        var gm = GM.instance;
        var dm = gm?.ILDevMenuManager;
        if (dm == null) { Plugin.Log.LogWarning("no ILDevMenuManager yet"); return; }

        gm.AllowDebugMenu = true;

        if (!menuGrafted || dm.rootMenuItem == null)
        {
            var root = gm.CreateDevMenu();
            if (root == null) { Plugin.Log.LogWarning("CreateDevMenu returned null"); return; }

            root.children = Append(root.children, TrainerMenu.Build());
            dm.Init(root);
            menuGrafted = true;
        }

        dm.Toggle();
    }

    // MenuItem.children is a fixed-size Il2Cpp array, so appending means rebuilding it.
    static Il2CppReferenceArray<MenuItem> Append(Il2CppReferenceArray<MenuItem> src, MenuItem extra)
    {
        int n = src?.Length ?? 0;
        var grown = new Il2CppReferenceArray<MenuItem>(n + 1);
        for (int i = 0; i < n; i++) grown[i] = src[i];
        grown[n] = extra;
        return grown;
    }

    internal static void Revive()
    {
        if (!Plugin.Allowed()) return;
        var gm = GM.instance;
        var f = gm?.LocalPlayerFighter;
        if (f != null) gm.RevivePlayerFighter(f);
    }

    internal static void Bots(bool hurt)
    {
        if (!Plugin.Allowed()) return;
        var gm = GM.instance;
        if (gm == null) return;
        if (hurt) gm.HurtAllBots();
        else gm.DespawnAllBots();
    }
}
