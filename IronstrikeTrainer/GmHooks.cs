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
    static bool sawInit, sawUpdate, menuGrafted, loggedApply;

    // Legacy UnityEngine.Input throws in this build (Input System only), so hotkeys go through
    // Unity.InputSystem. Probed once, then cached.
    enum InputMode { Unknown, Legacy, System, None }
    static InputMode mode = InputMode.Unknown;

    // Kept because it is the natural per-scene hook, but IL2CPP inlines it and it never fires in
    // practice -- verified against be.788. Flag upkeep is driven from Update instead.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GM), nameof(GM.InitScene))]
    static void InitScene()
    {
        if (!sawInit) { sawInit = true; Plugin.Log.LogInfo("HOOK CONFIRMED: GM.InitScene fired."); }
        menuGrafted = false;
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
        EnsureFlags();
        if (Plugin.C.EnableHotkeys.Value && mode != InputMode.None) Hotkeys();
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

    // The game clears these on scene load, and InitScene never fires, so re-assert them from
    // Update. Compares first and only writes on a mismatch, so the common case is eight reads.
    static void EnsureFlags()
    {
        var c = Plugin.C;
        if (GM.CheatHighDamage           == c.HighDamage.Value
         && GM.CheatFastRegen            == c.FastRegen.Value
         && GM.CheatLowCooldowns         == c.LowCooldowns.Value
         && GM.CheatAllIronstrikes       == c.AllIronstrikes.Value
         && GM.CheatDontSpawnIronstrikes == c.DontSpawnIronstrikes.Value
         && GM.CheatNoHealthbars         == c.NoHealthbars.Value
         && GM.CheatNoDamageNumbers      == c.NoDamageNumbers.Value
         && GM.CheatNoStatusEffecs       == c.NoStatusEffects.Value) return;

        Apply("drift");
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

        if (!loggedApply)
        {
            loggedApply = true;
            Plugin.Log.LogInfo($"applied ({why}); readback highDamage={GM.CheatHighDamage} " +
                               $"lowCooldowns={GM.CheatLowCooldowns} noHealthbars={GM.CheatNoHealthbars} " +
                               $"allowDebugMenu={GM.instance?.AllowDebugMenu}");
        }
    }

    static void Hotkeys()
    {
        if (mode == InputMode.Unknown) mode = Probe();
        if (mode == InputMode.None) return;

        try
        {
            if (Down(1)) ToggleDevMenu();
            else if (Down(2)) Apply("F2");
            else if (Down(3)) Revive();
            else if (Down(4)) Bots(true);
            else if (Down(5)) Bots(false);
        }
        catch (Exception e)
        {
            mode = InputMode.None;
            Plugin.Log.LogWarning($"hotkeys off ({e.GetType().Name}). Use the in-game dev menu.");
        }
    }

    static InputMode Probe()
    {
        try
        {
            Input.GetKeyDown(KeyCode.F1);
            Plugin.Log.LogInfo("input: legacy UnityEngine.Input");
            return InputMode.Legacy;
        }
        catch (Exception) { }

        try
        {
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                Plugin.Log.LogInfo("input: Unity.InputSystem (legacy Input unavailable)");
                return InputMode.System;
            }
            Plugin.Log.LogWarning("input: no keyboard device. Hotkeys off.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"input: unavailable ({e.GetType().Name}). Hotkeys off.");
        }
        return InputMode.None;
    }

    // n is the function-key number, 1..5.
    static bool Down(int n)
    {
        if (mode == InputMode.Legacy)
            return Input.GetKeyDown((KeyCode)((int)KeyCode.F1 + n - 1));

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;
        var k = n switch
        {
            1 => kb.f1Key, 2 => kb.f2Key, 3 => kb.f3Key,
            4 => kb.f4Key, 5 => kb.f5Key, _ => null,
        };
        return k != null && k.wasPressedThisFrame;
    }

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
