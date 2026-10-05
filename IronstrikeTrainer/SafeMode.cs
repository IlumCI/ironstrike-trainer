using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IronstrikeTrainer;

// Keeps a modded client out of public multiplayer while allowing solo and private (code) matches.
// This is the developer's own rule, from a note he left in the binary for modders: cheats in
// private games with friends are fine, cheats that reach public games are not.
//
// Public play has exactly two entry points on the main menu, and both are locked here:
//   Play  -> MainMenuUI.PressPlay   quick match, i.e. public matchmaking
//   HOST  -> MainMenuUI.PressHost   hosts a game strangers can matchmake into
// Private Match (PressPrivateMatch) and Solo (PressSolo) stay available. The matchmaking methods
// those public buttons lead to are blocked as well, in case anything else reaches them.
//
// How bans work, for context: a plain-text name list on emcneill.com, fetched and enforced
// client-side. The binary has no upload path at all, so the only real risk is a person seeing a
// modded player in a public game -- which is exactly what this closes.
[HarmonyPatch]
internal static class SafeMode
{
    static bool announced;
    static bool enteredPrivately;

    internal static bool On => Plugin.C.SafeMode.Value;

    // True from pressing Private Match until we are back in a Single session (haven or solo).
    internal static bool InPrivateSession => enteredPrivately;

    internal static void Announce()
    {
        if (announced) return;
        announced = true;
        Plugin.Log.LogMessage(On
            ? "SAFE MODE ON: Play and HOST locked; Solo and Private Match allowed."
            : "SAFE MODE OFF: public matchmaking is NOT blocked. Do not run cheats like this.");
    }

    // Refuse incoming connections unless we deliberately went private, so friends can join a
    // private match we host and nobody can join anything else. Holding this on unconditionally, as
    // an earlier version did, would have locked friends out of private matches too.
    internal static void Enforce()
    {
        bool refuse = On && !enteredPrivately;
        try { if (GM.TestAutoRefuseAllConnections != refuse) GM.TestAutoRefuseAllConnections = refuse; }
        catch { }
    }

    // ---------------------------------------------------------------- entry points

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressPlay))]
    static bool BlockPlay()
    {
        if (!On) return true;
        Plugin.Log.LogWarning("SAFE MODE: Play (quick match) is locked while the trainer is loaded.");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressHost))]
    static bool BlockHost()
    {
        if (!On) return true;
        Plugin.Log.LogWarning("SAFE MODE: HOST (public) is locked while the trainer is loaded.");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressPrivateMatch))]
    static void PrivateMatch()
    {
        enteredPrivately = true;
        Plugin.Log.LogInfo("private match: trainer stays enabled for this session");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressSolo))]
    static void Solo() => enteredPrivately = false;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLifecycle), nameof(NetworkLifecycle.StartMatchmaking))]
    static bool BlockMatchmaking()
    {
        if (!On) return true;
        Plugin.Log.LogWarning("SAFE MODE: blocked StartMatchmaking (public lobby).");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLifecycle), nameof(NetworkLifecycle.FindOrHostMatchmaking))]
    static bool BlockFindOrHost()
    {
        if (!On) return true;
        Plugin.Log.LogWarning("SAFE MODE: blocked FindOrHostMatchmaking (public lobby).");
        return false;
    }

    // Audit trail, and where a return to a Single session ends "private". Deliberately does not
    // log the session name: for a private match that is the join code, and this log is the file
    // people attach to bug reports.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLifecycle), nameof(NetworkLifecycle.StartGame))]
    static void LogStartGame(Fusion.GameMode mode)
    {
        if (mode == Fusion.GameMode.Single) enteredPrivately = false;
        Plugin.Log.LogMessage($"session starting: mode={mode} private={enteredPrivately}");
    }

    // ---------------------------------------------------------------- the buttons

    static readonly HashSet<string> PublicMethods = new() { "PressPlay", "PressHost" };
    static readonly Dictionary<IntPtr, string> originalLabel = new();
    const float LockedAlpha = 0.35f;

    // Re-applied every second rather than once: the game toggles these buttons itself (online
    // status, run-type choices), and a lock that only lasts until its next refresh is no lock.
    internal static void LockPublicButtons()
    {
        MainMenuUI mm = null;
        foreach (var m in Resources.FindObjectsOfTypeAll<MainMenuUI>())
            if (m != null && m.gameObject.scene.IsValid()) { mm = m; break; }
        if (mm == null) return;

        bool locked = On;
        int changed = 0;
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
        {
            if (!IsPublic(b)) continue;
            if (b.interactable != locked && Mathf.Approximately(Alpha(b), locked ? LockedAlpha : 1f)) continue;

            b.interactable = !locked;
            SetAlpha(b, locked ? LockedAlpha : 1f);
            changed++;

            var label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) continue;
            if (locked)
            {
                // The label is localised; strip that or I2 puts "Play" straight back.
                foreach (var c in label.GetComponents<Component>())
                    if (c != null && c.GetIl2CppType().Name == "Localize") UnityEngine.Object.Destroy(c);
                if (!originalLabel.ContainsKey(label.Pointer)) originalLabel[label.Pointer] = label.text;
                label.text = "LOCKED (MODDED)";
            }
            else if (originalLabel.TryGetValue(label.Pointer, out var orig))
                label.text = orig;
        }
        if (changed > 0) Plugin.Log.LogInfo($"{(locked ? "locked" : "unlocked")} {changed} public play button(s)");
    }

    static bool IsPublic(Button b)
    {
        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            if (PublicMethods.Contains(b.onClick.GetPersistentMethodName(i))) return true;
        return false;
    }

    static float Alpha(Button b)
    {
        var cg = b.GetComponent<CanvasGroup>();
        return cg != null ? cg.alpha : 1f;
    }

    static void SetAlpha(Button b, float alpha)
    {
        var cg = b.GetComponent<CanvasGroup>() ?? b.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = alpha;
    }
}
