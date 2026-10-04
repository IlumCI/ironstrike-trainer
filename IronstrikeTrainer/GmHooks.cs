using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Assets.Scripts.Utilities;

namespace IronstrikeTrainer;

/// <summary>
/// Harmony hooks on GM, the game's god-object (global namespace, in GameAssembly.dll).
///
/// Both targets are Unity message methods invoked from native code, so they cannot be inlined
/// away -- which makes them safe hook points under IL2CPP.
/// </summary>
[HarmonyPatch]
internal static class GmHooks
{
    private static bool _loggedInitScene;
    private static bool _loggedUpdate;
    private static bool _legacyInputDead;
    private static bool _menuInjected;

    // ---------------------------------------------------------------- InitScene

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GM), nameof(GM.InitScene))]
    private static void InitScene_Postfix()
    {
        if (!_loggedInitScene)
        {
            _loggedInitScene = true;
            Plugin.Log.LogInfo("HOOK CONFIRMED: GM.InitScene postfix fired.");
        }

        _menuInjected = false;   // dev menu is rebuilt per scene
        Apply("InitScene");
    }

    // ------------------------------------------------------------------- Update

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GM), nameof(GM.Update))]
    private static void Update_Postfix()
    {
        if (!_loggedUpdate)
        {
            _loggedUpdate = true;
            Plugin.Log.LogInfo("HOOK CONFIRMED: GM.Update postfix fired.");
        }

        if (!Plugin.C.Enabled.Value) return;

        // Cheap per-frame work only: VR runs at 72-120 Hz and allocation here causes judder.
        Cheats.Patches.EnforceGodMode();

        if (Plugin.C.EnableHotkeys.Value && !_legacyInputDead) PollHotkeys();
    }

    // -------------------------------------------------------------------- state

    /// <summary>
    /// Solo test. NetworkIsRunning() is NOT a solo test -- solo play still starts a Fusion Host
    /// session (GM.LoadAsyncSceneByIndex passes GameMode.Host). Player count is.
    /// </summary>
    internal static bool IsSolo()
    {
        try
        {
            var nl = GM.instance?.NetLifecycle;
            if (nl == null) return true;              // menus, loading
            if (!nl.NetworkIsRunning()) return true;  // network down: definitely alone
            return nl.SpawnedPlayerCount <= 1;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"IsSolo() failed, assuming NOT solo: {e.Message}");
            return false;                             // fail closed
        }
    }

    // -------------------------------------------------------------------- apply

    internal static void Apply(string reason)
    {
        if (!Plugin.C.Enabled.Value) return;

        if (!Plugin.Allowed())
        {
            Plugin.Log.LogInfo($"Apply({reason}) skipped: other players present and SoloOnly is on.");
            return;
        }

        try
        {
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

            Plugin.Log.LogInfo($"Applied ({reason}).");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Apply({reason}) failed: {e}");
        }
    }

    // ------------------------------------------------------------------ hotkeys

    private static void PollHotkeys()
    {
        try
        {
            if (Key(UnityEngine.KeyCode.F1)) ToggleDevMenu();
            else if (Key(UnityEngine.KeyCode.F2)) Apply("F2");
            else if (Key(UnityEngine.KeyCode.F3)) ReviveLocal();
            else if (Key(UnityEngine.KeyCode.F4)) Bots(hurt: true);
            else if (Key(UnityEngine.KeyCode.F5)) Bots(hurt: false);
        }
        catch (InvalidOperationException)
        {
            _legacyInputDead = true;
            Plugin.Log.LogWarning(
                "Legacy UnityEngine.Input is unavailable (Input System only build). Hotkeys off. " +
                "Open the dev menu with the in-game VR controller binding; the Trainer submenu " +
                "is inside it, so nothing is lost.");
        }
        catch (Exception e)
        {
            _legacyInputDead = true;
            Plugin.Log.LogError($"Hotkey polling disabled after error: {e}");
        }
    }

    private static bool Key(UnityEngine.KeyCode k) => UnityEngine.Input.GetKeyDown(k);

    // ------------------------------------------------------------------- verbs

    /// <summary>
    /// Open the dev menu with our Trainer submenu grafted on. MenuItem.children is a fixed-size
    /// Il2Cpp array, so appending means rebuilding it one element longer.
    /// </summary>
    internal static void ToggleDevMenu()
    {
        if (!Plugin.Allowed()) { Plugin.Log.LogInfo("Dev menu blocked: not solo."); return; }

        try
        {
            var gm = GM.instance;
            if (gm == null) { Plugin.Log.LogWarning("GM.instance is null."); return; }

            gm.AllowDebugMenu = true;

            var dm = gm.ILDevMenuManager;
            if (dm == null) { Plugin.Log.LogWarning("GM.instance.ILDevMenuManager is null."); return; }

            if (!_menuInjected || dm.rootMenuItem == null)
            {
                var root = gm.CreateDevMenu();
                if (root == null) { Plugin.Log.LogWarning("CreateDevMenu() returned null."); return; }

                var trainer = TrainerMenu.Build();
                root.children = Append(root.children, trainer);

                dm.Init(root);
                _menuInjected = true;
                Plugin.Log.LogInfo("Dev menu initialised with Trainer submenu grafted on.");
            }

            dm.Toggle();
            Plugin.Log.LogInfo($"Dev menu toggled -> enabled={dm.Enabled()}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"ToggleDevMenu failed: {e}");
        }
    }

    private static Il2CppReferenceArray<MenuItem> Append(
        Il2CppReferenceArray<MenuItem> existing, MenuItem extra)
    {
        int n = existing?.Length ?? 0;
        var grown = new Il2CppReferenceArray<MenuItem>(n + 1);
        for (int i = 0; i < n; i++) grown[i] = existing[i];
        grown[n] = extra;
        return grown;
    }

    internal static void ReviveLocal()
    {
        if (!Plugin.Allowed()) return;
        try
        {
            var gm = GM.instance;
            var f = gm?.LocalPlayerFighter;
            if (f == null) { Plugin.Log.LogInfo("No LocalPlayerFighter to revive."); return; }
            gm.RevivePlayerFighter(f);
            Plugin.Log.LogInfo("RevivePlayerFighter(local) called.");
        }
        catch (Exception e) { Plugin.Log.LogError($"Revive failed: {e}"); }
    }

    internal static void Bots(bool hurt)
    {
        if (!Plugin.Allowed()) return;
        try
        {
            var gm = GM.instance;
            if (gm == null) return;
            if (hurt) { gm.HurtAllBots();    Plugin.Log.LogInfo("HurtAllBots() called."); }
            else      { gm.DespawnAllBots(); Plugin.Log.LogInfo("DespawnAllBots() called."); }
        }
        catch (Exception e) { Plugin.Log.LogError($"Bots(hurt={hurt}) failed: {e}"); }
    }
}
