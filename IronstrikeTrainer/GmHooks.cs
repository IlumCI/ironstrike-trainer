using System;
using HarmonyLib;
using Assets.Scripts.Utilities;
using UnityEngine;

namespace IronstrikeTrainer;

// GM.InitScene and GM.Update are Unity messages called from native code, so IL2CPP can't inline
// them away. That makes them the safe hook points here.
[HarmonyPatch]
internal static class GmHooks
{
    static bool sawInit, sawUpdate, menuGrafted, loggedApply, autoOpened, loggedWhere;
    static float firstUpdateAt = -1f;

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
        Apply("InitScene");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GM), nameof(GM.Update))]
    static void Update()
    {
        if (!sawUpdate)
        {
            sawUpdate = true;
            Plugin.Log.LogInfo("HOOK CONFIRMED: GM.Update fired.");
            TamperWatch.Init(Plugin.C.WatchTamper.Value);
            SafeMode.Announce();
        }
        if (!Plugin.C.Enabled.Value) return;

        // Keep this cheap. VR runs 72-120Hz and allocating here shows up as judder.
        SafeMode.Enforce();
        Cheats.SyncFromConfig();
        Cheats.Patches.PinGodMode();
        EnsureFlags();
        TamperWatch.Tick();
        TrainerPanel.Tick();
        MaybeAutoOpen();
        if (Plugin.C.EnableHotkeys.Value && mode != InputMode.None) Hotkeys();
    }

    // NetworkIsRunning() is not a solo test: solo play still starts a Fusion Host session, since
    // GM.LoadAsyncSceneByIndex passes GameMode.Host. Player count is the real check.
    static void MaybeAutoOpen()
    {
        float after = Plugin.C.AutoOpenAfter.Value;
        if (after <= 0f || autoOpened) return;

        float now = UnityEngine.Time.realtimeSinceStartup;
        if (firstUpdateAt < 0f) firstUpdateAt = now;
        if (now - firstUpdateAt < after) return;

        autoOpened = true;

        if (Plugin.C.ProbeOptions.Value) { OptionsProbe.Run(); return; }

        Plugin.Log.LogInfo($"auto-opening trainer panel after {after}s");
        TrainerPanel.Toggle(null);
    }

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
            if (Down(1)) TrainerPanel.Toggle(null);
            else if (Down(2)) Apply("F2");
            else if (Down(3)) Revive();
            else if (Down(4)) Bots(true);
            else if (Down(5)) DespawnBots();
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

    internal static void Revive()
    {
        if (!Plugin.Allowed()) return;
        var gm = GM.instance;
        var f = gm?.LocalPlayerFighter;
        if (f != null) gm.RevivePlayerFighter(f);
    }

    // GM.DespawnAllBots() is a dev helper that does nothing in the retail scenes, so go through the
    // path the game itself uses: every registered AIBot's driver into NetworkGameMaster.DespawnBot.
    // Host-only in the game, which is fine: in solo play you are the host.
    internal static void DespawnBots()
    {
        if (!Plugin.Allowed()) return;
        var bots = AIHivemind.instance?.bots;
        var ngm = GM.instance?.NetGameMaster;
        if (bots == null || ngm == null) { Plugin.Log.LogInfo("despawn: no bots or no game master"); return; }

        // Snapshot first: DespawnBot deregisters, which mutates the list we are walking.
        var drivers = new System.Collections.Generic.List<NetworkBotFighterDriver>();
        for (int i = 0; i < bots.Count; i++) { var d = bots[i]?.nbfd; if (d != null) drivers.Add(d); }

        int n = 0;
        foreach (var d in drivers)
        {
            try { ngm.DespawnBot(d); n++; }
            catch (Exception e) { Plugin.Log.LogWarning($"despawn failed for one bot: {e.Message}"); }
        }
        Plugin.Log.LogInfo($"despawned {n}/{drivers.Count} bots");
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
