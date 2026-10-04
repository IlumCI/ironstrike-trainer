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

        // Same lever Smoke Bombs and the Invisibility spell pull.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalcSkillAndStatusEffectFlag))]
        static void StatFlag(Fighter __instance, SkillFlagType type, ref bool __result)
        {
            if (!Invisible || type != SkillFlagType.Targetable) return;
            if (!Plugin.Allowed() || !IsLocal(__instance)) return;
            __result = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalculateDamage))]
        static void Damage(HitInfo hitInfo, ref float __result)
        {
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
            if (!__result || !Plugin.Allowed()) return;
            if (f1 == Faction.Uninitialized || f2 == Faction.Uninitialized) return;

            if (TeamKillEnemies && f1 == Faction.EnemyBots && f2 == Faction.EnemyBots)
                __result = false;
            else if (TeamKillPlayers && PlayerSide(f1) && PlayerSide(f2))
                __result = false;
        }

        static void Tune(Projectile p, Fighter parent)
        {
            if (p == null || !Plugin.Allowed() || !IsLocal(parent)) return;

            if (!Off(ProjectileSpeed)) p.speed *= ProjectileSpeed;
            if (!Off(ProjectileRange))
            {
                p.maxLifeTime *= ProjectileRange;   // range is bounded by flight time
                p.gravity /= ProjectileRange;       // ...and by how fast the arc drops
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.SetTypes), new[] { typeof(Fighter) })]
        static void Spawned(Projectile __instance, Fighter parent) => Tune(__instance, parent);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.SetTypes),
                      new[] { typeof(Fighter), typeof(SpellType) })]
        static void SpawnedSpell(Projectile __instance, Fighter parent) => Tune(__instance, parent);
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
