using System;
using HarmonyLib;

namespace IronstrikeTrainer;

/// <summary>
/// Mutable trainer state plus the Harmony patches that enforce it.
///
/// Most value cheats route through one hook: <c>Fighter.CalcSkillAndStatusEffectValue</c>, the
/// game's single central stat query. Every stat the skill/status system can modify passes through
/// it keyed by <see cref="SkillCalcType"/>, so one postfix covers move speed, jump, weakspot range,
/// visibility, spell damage and dash in one place.
/// </summary>
internal static class Cheats
{
    // ---- toggles -----------------------------------------------------------
    public static bool GodMode;
    public static bool InstaKill;
    public static bool Invisible;

    /// <summary>Friendly fire among enemy bots (EnemyBots vs EnemyBots).</summary>
    public static bool TeamKillEnemies;

    /// <summary>Friendly fire on the player side (LocalPlayer/Allies vs each other).</summary>
    public static bool TeamKillPlayers;

    // ---- multipliers (1.0 == vanilla) -------------------------------------
    public static float MoveSpeed = 1f;
    public static float JumpHeight = 1f;
    public static float MeleeReach = 1f;
    public static float IronstrikeRate = 1f;   // higher = weakspots appear more often
    public static float ProjectileSpeed = 1f;
    public static float ProjectileRange = 1f;

    /// <summary>Step lists the menu cycles through. First entry is always vanilla.</summary>
    public static readonly float[] Steps = { 1f, 1.25f, 1.5f, 2f, 3f, 5f, 10f };

    public static float NextStep(float current)
    {
        for (int i = 0; i < Steps.Length; i++)
            if (Math.Abs(Steps[i] - current) < 0.001f)
                return Steps[(i + 1) % Steps.Length];
        return Steps[0];
    }

    internal static Fighter Local => GM.instance?.LocalPlayerFighter;

    private static bool IsPlayerSide(Faction f) => f == Faction.LocalPlayer || f == Faction.Allies;

    private static bool IsLocal(Fighter f) =>
        f != null && Local != null && f.Pointer == Local.Pointer;

    /// <summary>
    /// Scale a stat the game returns from the skill/status pipeline.
    ///
    /// We cannot read method bodies out of an IL2CPP build, so whether the game treats these as
    /// multipliers or as additive bonus-percentages is unverified. Multiplying alone would be a
    /// no-op whenever no skill contributes a base value (result 0), so we also floor the result at
    /// (factor - 1). That makes the knob bite under either interpretation. Tune the step list if a
    /// given stat turns out to be far too strong or too weak in practice.
    /// </summary>
    private static float Scale(float value, float factor)
    {
        if (Math.Abs(factor - 1f) < 0.001f) return value;
        float scaled = value * factor;
        float additive = factor - 1f;
        return scaled > additive ? scaled : additive;
    }

    // ======================================================================
    //  Patches
    // ======================================================================

    [HarmonyPatch]
    internal static class Patches
    {
        /// <summary>God mode. Pinned every frame because the game clears it on respawn/scene load.</summary>
        internal static void EnforceGodMode()
        {
            if (!GodMode) return;
            try
            {
                var f = Local;
                if (f != null) f.invulnerable = true;
            }
            catch { /* fighter not spawned yet */ }
        }

        /// <summary>The central stat hook — move speed, jump, weakspot range, invisibility.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalcSkillAndStatusEffectValue))]
        private static void CalcValue_Postfix(Fighter __instance, SkillCalcType type, ref float __result)
        {
            if (!Plugin.Allowed()) return;
            if (!IsLocal(__instance)) return;

            switch (type)
            {
                case SkillCalcType.MoveSpeed:
                    __result = Scale(__result, MoveSpeed);
                    break;
                case SkillCalcType.JumpVelocity:
                case SkillCalcType.JumpHorizontal:
                    __result = Scale(__result, JumpHeight);
                    break;
                case SkillCalcType.WeakspotRange:
                    __result = Scale(__result, IronstrikeRate);
                    break;
                case SkillCalcType.VisibilityPercent:
                    if (Invisible) __result = 0f;
                    break;
            }
        }

        /// <summary>
        /// Invisibility proper: the AI asks whether a fighter is targetable. Saying no is the same
        /// lever Smoke Bombs and the Invisibility spell pull.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalcSkillAndStatusEffectFlag))]
        private static void CalcFlag_Postfix(Fighter __instance, SkillFlagType type, ref bool __result)
        {
            if (!Plugin.Allowed()) return;
            if (!Invisible) return;
            if (type != SkillFlagType.Targetable) return;
            if (!IsLocal(__instance)) return;
            __result = false;
        }

        /// <summary>Insta-kill, but only for damage we deal. Never amplifies incoming damage.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalculateDamage))]
        private static void CalculateDamage_Postfix(HitInfo hitInfo, ref float __result)
        {
            if (!Plugin.Allowed()) return;
            if (!InstaKill || hitInfo == null) return;
            try
            {
                if (!IsLocal(hitInfo.attackingFighter)) return;   // our hits only
                if (IsLocal(hitInfo.hitFighter)) return;          // never ourselves
                __result = 999999f;
            }
            catch { }
        }

        /// <summary>
        /// Friendly fire. GM.isSameTeam is the game's single team check, so denying it for a
        /// faction pair makes hits between those fighters register as real damage -- which is
        /// exactly "enemy swings/projectiles that land on other enemies now count".
        ///
        /// Note this is the same predicate the AI consults for targeting, so enemies will also
        /// start deliberately fighting each other rather than merely hurting each other by
        /// accident. That is usually the fun version; if you want accidental-only, this would
        /// need to move down into the per-hit path instead.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GM), nameof(GM.isSameTeam))]
        private static void IsSameTeam_Postfix(Faction f1, Faction f2, ref bool __result)
        {
            if (!Plugin.Allowed()) return;
            if (!__result) return;                       // already enemies, nothing to do
            if (f1 == Faction.Uninitialized || f2 == Faction.Uninitialized) return;

            if (TeamKillEnemies && f1 == Faction.EnemyBots && f2 == Faction.EnemyBots)
            {
                __result = false;
                return;
            }

            if (TeamKillPlayers && IsPlayerSide(f1) && IsPlayerSide(f2))
                __result = false;
        }

        // -- projectiles: tune on spawn, when the owner is known ------------

        private static void TuneProjectile(Projectile p, Fighter parent)
        {
            if (!Plugin.Allowed() || p == null) return;
            if (!IsLocal(parent)) return;
            try
            {
                if (Math.Abs(ProjectileSpeed - 1f) > 0.001f)
                    p.speed *= ProjectileSpeed;

                if (Math.Abs(ProjectileRange - 1f) > 0.001f)
                {
                    // Range is bounded by flight time and by how fast the arc drops.
                    p.maxLifeTime *= ProjectileRange;
                    p.gravity /= ProjectileRange;
                }
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.SetTypes), new Type[] { typeof(Fighter) })]
        private static void SetTypes_Postfix(Projectile __instance, Fighter parent)
            => TuneProjectile(__instance, parent);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.SetTypes),
                      new Type[] { typeof(Fighter), typeof(SpellType) })]
        private static void SetTypesSpell_Postfix(Projectile __instance, Fighter parent)
            => TuneProjectile(__instance, parent);
    }

    // ======================================================================
    //  Direct pokes (no patch needed)
    // ======================================================================

    /// <summary>
    /// Melee reach and ironstrike frequency live on the equipped Weapon, so they are re-applied
    /// whenever gear changes rather than patched. <c>WeakspotBaseInterval</c> is the dev's own
    /// ironstrike cadence field: smaller interval means weakspots appear more often.
    /// </summary>
    public static void ApplyWeaponTweaks()
    {
        var f = Local;
        if (f == null) return;

        ApplyToWeapon(f.MainWeapon);
        ApplyToWeapon(f.OffWeapon);
    }

    private static readonly System.Collections.Generic.Dictionary<IntPtr, float> _baseInterval = new();
    private static readonly System.Collections.Generic.Dictionary<IntPtr, UnityEngine.Vector3> _baseScale = new();

    private static void ApplyToWeapon(Weapon w)
    {
        if (w == null) return;
        try
        {
            var key = w.Pointer;

            // Remember vanilla values once, so toggling back to 1.0x restores exactly.
            if (!_baseInterval.ContainsKey(key)) _baseInterval[key] = w.WeakspotBaseInterval;
            w.WeakspotBaseInterval = _baseInterval[key] / Math.Max(IronstrikeRate, 0.01f);

            var t = w.transform;
            if (t != null)
            {
                if (!_baseScale.ContainsKey(key)) _baseScale[key] = t.localScale;
                t.localScale = _baseScale[key] * MeleeReach;
            }
        }
        catch { }
    }
}
