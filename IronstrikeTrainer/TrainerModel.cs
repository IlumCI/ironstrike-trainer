using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace IronstrikeTrainer;

// What the trainer offers, independent of how it is drawn. Rows are described once here; a renderer
// (cloned Settings cards, or a standalone panel) just walks them. Keeps the UI decision reversible.
internal static class TrainerModel
{
    internal enum Kind { Toggle, Stepper, Action }

    internal sealed class Row
    {
        public Kind Kind;
        public string Group;
        public Func<string> Label;      // the name
        public Action Activate;         // click, or [+] on a stepper
        public Action Decrease;         // [-] on a stepper
        public Func<string> Value;      // stepper readout, e.g. "2x"
        public Func<bool> IsOn;         // Toggle only, for a checkbox visual
    }

    static readonly float[] Steps = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 3f, 5f, 10f };

    // Step to the neighbouring value, clamped at the ends. Values set by hand in the config that are
    // not on the ladder snap to the nearest rung in the requested direction.
    static float Step(float cur, int dir)
    {
        if (dir > 0) { foreach (var v in Steps) if (v > cur + 0.001f) return v; return Steps[^1]; }
        for (int i = Steps.Length - 1; i >= 0; i--) if (Steps[i] < cur - 0.001f) return Steps[i];
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
        Kind = Kind.Stepper, Group = group,
        Label = () => name,
        Value = () => $"{e.Value:0.##}x",
        Activate = () => e.Value = Step(e.Value, +1),
        Decrease = () => e.Value = Step(e.Value, -1),
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

                Toggle("Team Kill", "Enemies hurt each other", c.TeamKillEnemies),
                // Needs other players, and the trainer is solo-only, so there is nothing for it to act
                // on yet. Kept visible and labelled rather than silently missing.
                Toggle("Team Kill", "Players hurt each other (co-op, not yet)", c.TeamKillPlayers),

                Toggle("Visual", "Hide Healthbars", c.NoHealthbars),
                Toggle("Visual", "Hide Damage Numbers", c.NoDamageNumbers),
                Toggle("Visual", "Hide Status Effects", c.NoStatusEffects),

                Act   ("Bots", "Hurt All Bots", () => GmHooks.Bots(true)),
                Act   ("Bots", "Despawn All Bots", GmHooks.DespawnBots),
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
