using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace IronstrikeTrainer;

// Most value cheats ride one hook: Fighter.CalcSkillAndStatusEffectValue, which every
// skill/status-modifiable stat passes through keyed by SkillCalcType.
internal static class Cheats
{
    public static bool GodMode, InstaKill, Invisible;
    public static bool TeamKillEnemies, TeamKillPlayers;

    public static float MoveSpeed = 1f, JumpHeight = 1f, MeleeReach = 1f;
    public static float IronstrikeRate = 1f, ProjectileSpeed = 1f, ProjectileRange = 1f;

    public static readonly float[] Steps = { 1f, 1.25f, 1.5f, 2f, 3f, 5f, 10f };

    public static float NextStep(float cur)
    {
        for (int i = 0; i < Steps.Length; i++)
            if (Mathf.Abs(Steps[i] - cur) < 0.001f) return Steps[(i + 1) % Steps.Length];
        return Steps[0];
    }

    // Config is the control surface until the custom UI exists. Menu code writes the same fields.
    public static void SyncFromConfig()
    {
        var c = Plugin.C;
        GodMode         = c.GodMode.Value;
        InstaKill       = c.InstaKill.Value;
        Invisible       = c.Invisible.Value;
        TeamKillEnemies = c.TeamKillEnemies.Value;
        TeamKillPlayers = c.TeamKillPlayers.Value;

        if (!Same(MoveSpeed, c.MoveSpeed.Value) || !Same(MeleeReach, c.MeleeReach.Value)
         || !Same(IronstrikeRate, c.IronstrikeRate.Value))
        {
            MoveSpeed      = c.MoveSpeed.Value;
            MeleeReach     = c.MeleeReach.Value;
            IronstrikeRate = c.IronstrikeRate.Value;
            ApplyWeaponTweaks();          // reach and ironstrike cadence live on the weapon
        }
        JumpHeight      = c.JumpHeight.Value;
        ProjectileSpeed = c.ProjectileSpeed.Value;
        ProjectileRange = c.ProjectileRange.Value;

        UpdateInvisibility();
    }

    static bool Same(float a, float b) => Mathf.Abs(a - b) < 0.0001f;

    // Each patch announces its first real hit. Under IL2CPP an inlined target applies cleanly and
    // never fires -- GM.InitScene does exactly that -- so "it compiled" proves nothing.
    static readonly System.Collections.Generic.HashSet<string> fired = new();
    internal static void FirstHit(string what)
    {
        if (fired.Add(what)) Plugin.Log.LogInfo($"PATCH LIVE: {what}");
    }

    internal static Fighter Local => GM.instance?.LocalPlayerFighter;

    static bool IsLocal(Fighter f) => f != null && Local != null && f.Pointer == Local.Pointer;
    static bool PlayerSide(Faction f) => f == Faction.LocalPlayer || f == Faction.Allies;
    static bool Off(float f) => Mathf.Abs(f - 1f) < 0.001f;

    // IL2CPP strips method bodies, so whether the game reads these stats as multipliers or as
    // additive bonus-percent is unknowable from the binary. Multiply, but floor at (factor-1) so
    // the knob still bites when no skill contributes a base value. Retune Steps if a stat lands
    // way off.
    static float Scale(float v, float factor)
    {
        if (Off(factor)) return v;
        return Mathf.Max(v * factor, factor - 1f);
    }

    [HarmonyPatch]
    internal static class Patches
    {
        // Cleared on respawn and scene load, so it gets pinned from GM.Update.
        internal static void PinGodMode()
        {
            if (!GodMode) return;
            try
            {
                var f = Local;
                if (f != null) f.invulnerable = true;
            }
            catch (Exception) { }   // fighter despawned mid-frame
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalcSkillAndStatusEffectValue))]
        static void StatValue(Fighter __instance, SkillCalcType type, ref float __result)
        {
            if (!Plugin.Allowed() || !IsLocal(__instance)) return;

            FirstHit($"Fighter.CalcSkillAndStatusEffectValue({type})");

            switch (type)
            {
                case SkillCalcType.MoveSpeed:
                    __result = Scale(__result, MoveSpeed); break;
                case SkillCalcType.JumpVelocity:
                case SkillCalcType.JumpHorizontal:
                    __result = Scale(__result, JumpHeight); break;
                case SkillCalcType.WeakspotRange:
                    __result = Scale(__result, IronstrikeRate); break;
                case SkillCalcType.VisibilityPercent:
                    if (Invisible) __result = 0f;
                    break;
            }
        }

        // Deliberately no longer denies SkillFlagType.Targetable. Doing that left the AI with no
        // valid target at all, and with nothing to score the bots simply froze. The game ships
        // StatusType.Invisibility, so use that instead and let the AI behave as it does against the
        // real spell. Kept as a patch point only for the PATCH LIVE telemetry.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalcSkillAndStatusEffectFlag))]
        static void StatFlag(Fighter __instance, SkillFlagType type, ref bool __result)
        {
            if (type == SkillFlagType.Targetable) FirstHit("Fighter.CalcSkillAndStatusEffectFlag(Targetable)");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalculateDamage))]
        static void Damage(HitInfo hitInfo, ref float __result)
        {
            FirstHit("Fighter.CalculateDamage");
            if (!InstaKill || hitInfo == null || !Plugin.Allowed()) return;
            if (!IsLocal(hitInfo.attackingFighter)) return;   // our hits only
            if (IsLocal(hitInfo.hitFighter)) return;          // never ourselves
            __result = 999999f;
        }

        // isSameTeam is also what the AI consults to pick targets, so enemies will actively fight
        // each other rather than only clipping each other by accident.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GM), nameof(GM.isSameTeam))]
        static void SameTeam(Faction f1, Faction f2, ref bool __result)
        {
            FirstHit("GM.isSameTeam");
            if (!__result || !Plugin.Allowed()) return;
            if (f1 == Faction.Uninitialized || f2 == Faction.Uninitialized) return;

            if (TeamKillEnemies && f1 == Faction.EnemyBots && f2 == Faction.EnemyBots)
                __result = false;
            else if (TeamKillPlayers && PlayerSide(f1) && PlayerSide(f2))
                __result = false;
        }

        static void Tune(Projectile p, Fighter parent)
        {
            FirstHit("Projectile.SetTypes");
            if (p == null || !Plugin.Allowed() || !IsLocal(parent)) return;

            // Homing projectiles are steered by the game each tick; rescaling speed or gravity
            // under it makes the solver overshoot its target, so leave those alone and only
            // extend how long they live.
            bool homing = p.forceSeeking || p.percentSeeking > 0f || p.target != null;

            if (!Off(ProjectileRange)) p.maxLifeTime *= ProjectileRange;

            if (homing) return;

            if (!Off(ProjectileSpeed)) p.speed *= ProjectileSpeed;
            if (!Off(ProjectileRange)) p.gravity /= ProjectileRange;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.SetTypes), new[] { typeof(Fighter) })]
        static void Spawned(Projectile __instance, Fighter parent) => Tune(__instance, parent);

        // Full signature, including spellType. Omitting a trailing parameter is fine for managed
        // Harmony but not for an IL2CPP native detour: spell projectiles (fireball, seeking arrows)
        // take this overload and the mismatch hard-crashed the game on spawn.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.SetTypes),
                      new[] { typeof(Fighter), typeof(SpellType) })]
        static void SpawnedSpell(Projectile __instance, Fighter parent, SpellType spellType)
            => Tune(__instance, parent);
    }

    // Invisibility via the game's own StatusType.Invisibility. Re-applied on a rolling window so
    // it persists while enabled and lapses on its own shortly after being switched off, which also
    // means we never have to remove it by hand.
    const float InvisWindow = 3f;
    static int invisRefreshAt;
    static int invisFailures;
    static float invisQuietUntil;

    static void UpdateInvisibility()
    {
        if (UnityEngine.Time.realtimeSinceStartup < invisQuietUntil) return;

        try
        {
            var me = Local;
            var sm = StatusManager.instance;
            if (me == null || sm == null) return;

            // Status effects are tick-scheduled, so there has to be a running simulation. Outside
            // one, TimeHelper.CurrentTick has no runner to read and throws.
            var nl = GM.instance?.NetLifecycle;
            if (nl == null || !nl.NetworkIsRunning()) return;

            int now = TimeHelper.CurrentTick;

            if (!Invisible || !Plugin.Allowed())
            {
                if (invisRefreshAt != 0)
                {
                    sm.RemoveStatusEffectFromFighter(StatusType.Invisibility, me, me);
                    invisRefreshAt = 0;
                    Plugin.Log.LogInfo("invisibility cleared");
                }
                return;
            }

            if (now < invisRefreshAt) return;

            sm.GiveStatusEffectToFighter(StatusType.Invisibility, me, me,
                                         now + TimeHelper.GetTicks(InvisWindow), 1f);
            invisRefreshAt = now + TimeHelper.GetTicks(InvisWindow * 0.5f);
            FirstHit("StatusManager.GiveStatusEffectToFighter(Invisibility)");
        }
        catch (Exception e)
        {
            // Back off instead of retrying every frame. Clearing Invisible here is pointless:
            // SyncFromConfig reinstates it from config on the very next frame.
            invisFailures++;
            invisQuietUntil = UnityEngine.Time.realtimeSinceStartup + 5f;
            if (invisFailures <= 3)
                Plugin.Log.LogWarning($"invisibility deferred ({e.GetType().Name}), retrying in 5s");
            else if (invisFailures == 4)
                Plugin.Log.LogWarning("invisibility still failing; suppressing further warnings");
        }
    }

    // Reach and ironstrike cadence live on the equipped Weapon, so they are re-applied on gear
    // change instead of patched. WeakspotBaseInterval is the dev's own cadence field: smaller
    // interval means weakspots show up more often.
    static readonly Dictionary<IntPtr, float> baseInterval = new();
    static readonly Dictionary<IntPtr, Vector3> baseScale = new();

    public static void ApplyWeaponTweaks()
    {
        try
        {
            var f = Local;
            if (f == null) return;
            Apply(f.MainWeapon);
            Apply(f.OffWeapon);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"weapon tweak skipped: {e.Message}");
        }
    }

    static void Apply(Weapon w)
    {
        if (w == null) return;
        var key = w.Pointer;

        if (!baseInterval.ContainsKey(key)) baseInterval[key] = w.WeakspotBaseInterval;
        w.WeakspotBaseInterval = baseInterval[key] / Mathf.Max(IronstrikeRate, 0.01f);

        var t = w.transform;
        if (t == null) return;
        if (!baseScale.ContainsKey(key)) baseScale[key] = t.localScale;
        t.localScale = baseScale[key] * MeleeReach;
    }
}
