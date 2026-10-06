using System;
using System.Collections.Generic;
using System.Reflection;

namespace IronstrikeTrainer;

// For other plugins, read by reflection so neither side needs a hard dependency.
// IronstrikeServers shows ActiveRules() in its server listing, so joiners know what they are walking
// into before they connect.
public static class Api
{
    public static string ActiveRules()
    {
        var c = Plugin.C;
        if (c == null || !c.Enabled.Value) return "";
        var on = new List<string>();
        if (c.GodMode.Value) on.Add("god mode");
        if (c.InstaKill.Value) on.Add("insta-kill");
        if (c.Invisible.Value) on.Add("invisible");
        if (c.HighDamage.Value) on.Add("high damage");
        if (c.FastRegen.Value) on.Add("fast regen");
        if (c.LowCooldowns.Value) on.Add("low cooldowns");
        if (c.TeamKillEnemies.Value) on.Add("enemy infighting");
        if (c.MoveSpeed.Value != 1f || c.JumpHeight.Value != 1f) on.Add("movement x");
        if (c.ProjectileSpeed.Value != 1f || c.ProjectileRange.Value != 1f || c.MeleeReach.Value != 1f) on.Add("weapon x");
        return on.Count == 0 ? "trainer: no cheats" : "trainer: " + string.Join(", ", on);
    }
}

// IronstrikeServers runs modded servers in their own lobby, unjoinable without the mod. Those count
// as private for the trainer, the same as a Private Match code.
internal static class ServersBridge
{
    static PropertyInfo inModded;
    static bool looked;

    internal static bool InModdedSession()
    {
        try
        {
            if (!looked)
            {
                looked = true;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType("IronstrikeServers.Api", false);
                    if (t != null) { inModded = t.GetProperty("InModdedSession", BindingFlags.Public | BindingFlags.Static); break; }
                }
            }
            return inModded != null && (bool)inModded.GetValue(null);
        }
        catch (Exception) { return false; }
    }
}
