using System;

namespace IronstrikeTrainer;

// Read-only observation of the game's own anti-tamper state. GM.pleaseDontHack... is the field
// that guards CreateDevMenu; watching it tells us directly whether an action armed the guard,
// instead of guessing from symptoms like a forced run loss.
internal static class TamperWatch
{
    static bool enabled, flagged, banbu;
    static float dcd = float.NaN;
    static string unbu = "\0", dnbu = "\0";
    static ulong savedId;
    static int frame;

    public static void Init(bool on)
    {
        enabled = on;
        if (!on) return;
        Dump("initial");
    }

    public static void Tick()
    {
        if (!enabled) return;
        if (++frame % 120 != 0) return;      // ~every 2s at 60fps

        try
        {
            if (GM.pleaseDontHackImAPoorDevWithAFamilyToSupport != flagged
             || GM.banbu != banbu
             || !Eq(GM.dcd, dcd)
             || GM.unbu != unbu
             || GM.dnbu != dnbu
             || GM.savedID != savedId)
                Dump("CHANGED");
        }
        catch (Exception e)
        {
            enabled = false;
            Plugin.Log.LogWarning($"tamper watch off: {e.Message}");
        }
    }

    static bool Eq(float a, float b) => (float.IsNaN(a) && float.IsNaN(b)) || Math.Abs(a - b) < 0.0001f;

    static void Dump(string why)
    {
        flagged = GM.pleaseDontHackImAPoorDevWithAFamilyToSupport;
        banbu   = GM.banbu;
        dcd     = GM.dcd;
        unbu    = GM.unbu;
        dnbu    = GM.dnbu;
        savedId = GM.savedID;

        Plugin.Log.LogWarning(
            $"[tamper {why}] guard={flagged} banbu={banbu} dcd={dcd} " +
            $"unbu={Show(unbu)} dnbu={Show(dnbu)} savedID={savedId}");
    }

    static string Show(string s) => s == null ? "<null>" : s.Length == 0 ? "<empty>" : $"\"{s}\"";
}
