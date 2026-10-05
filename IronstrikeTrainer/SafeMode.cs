using System;
using HarmonyLib;

namespace IronstrikeTrainer;

// Keeps a modded client out of public multiplayer, structurally rather than by remembering to be
// careful. This exists because of the developer's request in the binary: cheats in private games
// are fine, cheats that reach public lobbies are not.
//
// Worth knowing how bans actually work here, because it shapes the risk. The list is a plain text
// file at emcneill.com fetched by display name and enforced client-side; GameAssembly.dll contains
// no UploadHandler and no UnityWebRequest.Post, so there is no telemetry and no automated
// detection. The only way to get banned is for a human to notice you in a public game. So the
// entire risk surface is "did a modded client reach strangers", and that is what this closes.
[HarmonyPatch]
internal static class SafeMode
{
    static bool announced;

    internal static bool On => Plugin.C.SafeMode.Value;

    internal static void Announce()
    {
        if (announced) return;
        announced = true;
        Plugin.Log.LogMessage(On
            ? "SAFE MODE ON: public matchmaking blocked, incoming connections refused."
            : "SAFE MODE OFF: public matchmaking is NOT blocked. Do not run cheats like this.");
    }

    // Each frame while safe mode is on, hold the dev's own refuse-all-connections switch down so
    // nobody can join a session we are hosting.
    internal static void Enforce()
    {
        if (!On) return;
        try
        {
            if (!GM.TestAutoRefuseAllConnections) GM.TestAutoRefuseAllConnections = true;
        }
        catch { }
    }

    // StartMatchmaking and FindOrHostMatchmaking are the only paths that can put us in a session
    // with strangers. Everything else (tutorial, solo, invited friends) goes through StartGame.
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

    // Audit trail. The sessionName parameter defaults to "public" throughout the game, so seeing
    // what we actually start is worth a log line even when nothing is blocked.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLifecycle), nameof(NetworkLifecycle.StartGame))]
    static void LogStartGame(Fusion.GameMode mode, string sessionName)
        => Plugin.Log.LogMessage($"session starting: mode={mode} name=\"{sessionName}\"");
}
