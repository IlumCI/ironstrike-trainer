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
            $"unbu={Redact(unbu)} dnbu={Redact(dnbu)} savedID={Redact(savedId)}");
    }

    // dnbu holds the local Steam persona name and savedID looks like an account id. The log file is
    // the first thing anyone attaches to a bug report, so these never go in verbatim. A short digest
    // still lets us see that a value changed, which is all the diagnostic needs.
    static string Redact(string v)
    {
        if (v == null) return "<null>";
        if (v.Length == 0) return "<empty>";
        return $"<len{v.Length}:{Digest(v)}>";
    }

    static string Redact(ulong v) => v == 0 ? "0" : $"<set:{Digest(v.ToString())}>";

    static string Digest(string v)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in v) { h ^= c; h *= 16777619u; }
            return h.ToString("x8").Substring(0, 6);
        }
    }
}
