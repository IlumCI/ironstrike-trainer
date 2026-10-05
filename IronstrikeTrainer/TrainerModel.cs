using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace IronstrikeTrainer;

// What the trainer offers, independent of how it is drawn. Rows are described once here; a renderer
// (cloned Settings cards, or a standalone panel) just walks them. Keeps the UI decision reversible.
internal static class TrainerModel
{
    internal enum Kind { Toggle, Cycle, Action }

    internal sealed class Row
    {
        public Kind Kind;
        public string Group;
        public Func<string> Label;      // full display text, state included
        public Action Activate;         // what a click does
        public Func<bool> IsOn;         // Toggle only, for a checkbox visual
    }

    static readonly float[] Steps = { 1f, 1.25f, 1.5f, 2f, 3f, 5f, 10f };

    static float Next(float cur)
    {
        for (int i = 0; i < Steps.Length; i++)
            if (Math.Abs(Steps[i] - cur) < 0.001f) return Steps[(i + 1) % Steps.Length];
        return Steps[0];
    }

    static Row Toggle(string group, string name, ConfigEntry<bool> e) => new Row
    {
        Kind = Kind.Toggle, Group = group, IsOn = () => e.Value,
        Label = () => name,
        Activate = () => e.Value = !e.Value,
    };

    static Row Cycle(string group, string name, ConfigEntry<float> e) => new Row
    {
        Kind = Kind.Cycle, Group = group,
        Label = () => $"{name}   {e.Value:0.##}x",
        Activate = () => e.Value = Next(e.Value),
    };

    static Row Act(string group, string name, Action fn) => new Row
    {
        Kind = Kind.Action, Group = group, Label = () => name, Activate = fn,
    };

    static List<Row> rows;

    public static List<Row> Rows
    {
        get
        {
            if (rows != null) return rows;
            var c = Plugin.C;
            rows = new List<Row>
            {
                Toggle("Survival", "God Mode", c.GodMode),
                Toggle("Survival", "Invisible", c.Invisible),
                Toggle("Survival", "Fast Regen", c.FastRegen),
                Act   ("Survival", "Revive Me", GmHooks.Revive),

                Toggle("Offense", "Insta-Kill", c.InstaKill),
                Toggle("Offense", "High Damage", c.HighDamage),
                Toggle("Offense", "Low Cooldowns", c.LowCooldowns),
                Toggle("Offense", "All Ironstrikes", c.AllIronstrikes),
                Cycle ("Offense", "Ironstrike Rate", c.IronstrikeRate),

                Cycle ("Movement", "Move Speed", c.MoveSpeed),
                Cycle ("Movement", "Jump Height", c.JumpHeight),

                Cycle ("Weapons", "Melee Reach", c.MeleeReach),
                Cycle ("Weapons", "Projectile Speed", c.ProjectileSpeed),
                Cycle ("Weapons", "Projectile Range", c.ProjectileRange),

                // Both of these are inert right now: GM.isSameTeam is inlined by IL2CPP and the
                // patch never fires. Left visible so the gap is obvious rather than silent.
                Toggle("Team Kill (not working)", "Enemies hurt each other", c.TeamKillEnemies),
                Toggle("Team Kill (not working)", "Player side hurts each other", c.TeamKillPlayers),

                Toggle("Visual", "Hide Healthbars", c.NoHealthbars),
                Toggle("Visual", "Hide Damage Numbers", c.NoDamageNumbers),
                Toggle("Visual", "Hide Status Effects", c.NoStatusEffects),

                Act   ("Bots", "Hurt All Bots", () => GmHooks.Bots(true)),
                Act   ("Bots", "Despawn All Bots", () => GmHooks.Bots(false)),
                Act   ("Bots", "Spawn Dummy", () => GM.instance?.SpawnDummyPlayer()),

                Act   ("Reset", "Reset All To Vanilla", ResetAll),
            };
            return rows;
        }
    }

    static void ResetAll()
    {
        var c = Plugin.C;
        c.GodMode.Value = c.Invisible.Value = c.InstaKill.Value = false;
        c.TeamKillEnemies.Value = c.TeamKillPlayers.Value = false;
        c.HighDamage.Value = c.FastRegen.Value = c.LowCooldowns.Value = false;
        c.AllIronstrikes.Value = c.DontSpawnIronstrikes.Value = false;
        c.NoHealthbars.Value = c.NoDamageNumbers.Value = c.NoStatusEffects.Value = false;
        c.MoveSpeed.Value = c.JumpHeight.Value = c.MeleeReach.Value = 1f;
        c.IronstrikeRate.Value = c.ProjectileSpeed.Value = c.ProjectileRange.Value = 1f;
        Plugin.Log.LogInfo("trainer reset to vanilla");
    }
}
