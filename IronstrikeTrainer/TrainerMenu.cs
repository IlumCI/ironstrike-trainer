using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Assets.Scripts.Utilities;

namespace IronstrikeTrainer;

// Grafts onto the dev's own MenuItem tree rather than drawing a new UI. ILDevMenuManager is
// already a VR text menu on the right controller, so we inherit his debounce and anchoring.
// Toggles render [x]/[ ]; a MenuItem carries one action, so multipliers cycle a step list.
internal static class TrainerMenu
{
    // Il2Cpp delegates die with their managed source: drop the reference and the GC takes the
    // trampoline out from under the menu. These lists are load-bearing.
    static readonly List<object> alive = new();
    static readonly List<Action> relabel = new();

    static Il2CppSystem.Action Act(Action fn)
    {
        var d = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() =>
        {
            try { fn(); Redraw(); }
            catch (Exception e) { Plugin.Log.LogError($"menu action: {e}"); }
        }));
        alive.Add(d);
        return d;
    }

    // Live label, refreshed after every action.
    static MenuItem Item(Func<string> label, Action onSelect)
    {
        var it = new MenuItem(label(), Act(onSelect));
        relabel.Add(() => { try { it.text = label(); } catch { } });
        alive.Add(it);
        return it;
    }

    // Fixed label. Pickers must use this: they rebuild on every open, and registering a refresher
    // per entry would grow `relabel` without bound and slow down every later action.
    static MenuItem Fixed(string label, Action onSelect)
    {
        var it = new MenuItem(label, Act(onSelect));
        alive.Add(it);
        return it;
    }

    static MenuItem Sub(string text, List<MenuItem> kids)
    {
        var arr = new Il2CppReferenceArray<MenuItem>(kids.Count);
        for (int i = 0; i < kids.Count; i++) arr[i] = kids[i];
        var it = new MenuItem(text, arr);
        alive.Add(it);
        return it;
    }

    static MenuItem Lazy(string text, Func<MenuItem> build)
    {
        var f = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<MenuItem>>(new Func<MenuItem>(build));
        alive.Add(f);
        var it = new MenuItem(text, f);
        alive.Add(it);
        return it;
    }

    static void Redraw()
    {
        foreach (var r in relabel) r();
        var dm = GM.instance?.ILDevMenuManager;
        if (dm == null) return;
        dm.needsUpdate = true;
        dm.UpdateMainText();
    }

    static string Tick(bool on) => on ? "[x] " : "[ ] ";
    static string X(float v) => $"< {v:0.##}x >";

    static void Flip(BepInEx.Configuration.ConfigEntry<bool> e)
    {
        e.Value = !e.Value;
        GmHooks.Apply("menu");
    }

    public static MenuItem Build()
    {
        alive.Clear();
        relabel.Clear();
        var c = Plugin.C;

        var survival = new List<MenuItem>
        {
            Item(() => Tick(Cheats.GodMode) + "God Mode", () => Cheats.GodMode = !Cheats.GodMode),
            Item(() => Tick(Cheats.Invisible) + "Invisible", () => Cheats.Invisible = !Cheats.Invisible),
            Item(() => Tick(c.FastRegen.Value) + "Fast Regen", () => Flip(c.FastRegen)),
            Item(() => "Revive Me", GmHooks.Revive),
        };

        var offense = new List<MenuItem>
        {
            Item(() => Tick(Cheats.InstaKill) + "Insta-Kill", () => Cheats.InstaKill = !Cheats.InstaKill),
            Item(() => Tick(c.HighDamage.Value) + "High Damage", () => Flip(c.HighDamage)),
            Item(() => Tick(c.LowCooldowns.Value) + "Low Cooldowns", () => Flip(c.LowCooldowns)),
            Item(() => Tick(c.AllIronstrikes.Value) + "All Ironstrikes", () => Flip(c.AllIronstrikes)),
            Item(() => "Ironstrike Rate  " + X(Cheats.IronstrikeRate), () =>
            {
                Cheats.IronstrikeRate = Cheats.NextStep(Cheats.IronstrikeRate);
                Cheats.ApplyWeaponTweaks();
            }),
        };

        var movement = new List<MenuItem>
        {
            Item(() => "Move Speed   " + X(Cheats.MoveSpeed),
                 () => Cheats.MoveSpeed = Cheats.NextStep(Cheats.MoveSpeed)),
            Item(() => "Jump Height  " + X(Cheats.JumpHeight),
                 () => Cheats.JumpHeight = Cheats.NextStep(Cheats.JumpHeight)),
        };

        var weapons = new List<MenuItem>
        {
            Item(() => "Melee Reach  " + X(Cheats.MeleeReach), () =>
            {
                Cheats.MeleeReach = Cheats.NextStep(Cheats.MeleeReach);
                Cheats.ApplyWeaponTweaks();
            }),
            Item(() => "Proj Speed   " + X(Cheats.ProjectileSpeed),
                 () => Cheats.ProjectileSpeed = Cheats.NextStep(Cheats.ProjectileSpeed)),
            Item(() => "Proj Range   " + X(Cheats.ProjectileRange),
                 () => Cheats.ProjectileRange = Cheats.NextStep(Cheats.ProjectileRange)),
        };

        var teamkill = new List<MenuItem>
        {
            Item(() => Tick(Cheats.TeamKillEnemies) + "Enemies hurt each other",
                 () => Cheats.TeamKillEnemies = !Cheats.TeamKillEnemies),
            Item(() => Tick(Cheats.TeamKillPlayers) + "Player side hurts each other",
                 () => Cheats.TeamKillPlayers = !Cheats.TeamKillPlayers),
        };

        var visual = new List<MenuItem>
        {
            Item(() => Tick(c.NoHealthbars.Value) + "Hide Healthbars", () => Flip(c.NoHealthbars)),
            Item(() => Tick(c.NoDamageNumbers.Value) + "Hide Damage Numbers", () => Flip(c.NoDamageNumbers)),
            Item(() => Tick(c.NoStatusEffects.Value) + "Hide Status Effects", () => Flip(c.NoStatusEffects)),
        };

        var bots = new List<MenuItem>
        {
            Item(() => "Hurt All Bots", () => GmHooks.Bots(true)),
            Item(() => "Despawn All Bots", () => GmHooks.Bots(false)),
            Item(() => "Spawn Dummy", () => GM.instance?.SpawnDummyPlayer()),
        };

        return Sub("Trainer", new List<MenuItem>
        {
            Sub("Survival", survival),
            Sub("Offense", offense),
            Sub("Movement", movement),
            Sub("Weapons", weapons),
            Sub("Team Kill", teamkill),
            Sub("Visual", visual),
            Sub("Bots", bots),
            Lazy("Give Skill...", Skills),
            Lazy("Give Weapon Set...", Weapons),
            Item(() => "Reset All", Reset),
        });
    }

    static void Reset()
    {
        Cheats.GodMode = Cheats.InstaKill = Cheats.Invisible = false;
        Cheats.TeamKillEnemies = Cheats.TeamKillPlayers = false;
        Cheats.MoveSpeed = Cheats.JumpHeight = Cheats.MeleeReach = 1f;
        Cheats.IronstrikeRate = Cheats.ProjectileSpeed = Cheats.ProjectileRange = 1f;

        var c = Plugin.C;
        c.HighDamage.Value = c.FastRegen.Value = c.LowCooldowns.Value = false;
        c.AllIronstrikes.Value = c.NoHealthbars.Value = false;
        c.NoDamageNumbers.Value = c.NoStatusEffects.Value = false;

        Cheats.ApplyWeaponTweaks();
        GmHooks.Apply("reset");
    }

    // Both pickers are built on open: fighterClass is unknown until a run starts.

    static MenuItem Skills()
    {
        var kids = new List<MenuItem>();
        try
        {
            var sm = SkillManager.instance;
            var me = Cheats.Local;
            if (sm == null || me == null) return Sub("Give Skill (no run)", kids);

            var cls = me.fighterClass;
            var all = sm.GetSkills();
            for (int i = 0; i < all.Count; i++)
            {
                var sk = all.get_Item(i);
                if (sk == null || sk.hidden || sk.skillClass != cls) continue;

                var type = sk.skillType;
                var lvl = sk.allowsEnhanced ? 10 : 5;
                kids.Add(Fixed($"{sk.skillName} (L{lvl})",
                               () => sm.GiveSkillToFighter(type, Cheats.Local, lvl)));
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"skill picker: {e}"); }

        return Sub($"Give Skill ({kids.Count})", kids);
    }

    static MenuItem Weapons()
    {
        var tiers = new List<MenuItem>();
        try
        {
            var am = ArmoryManager.instance;
            var me = Cheats.Local;
            if (am?.weaponSetDatabase == null || me == null) return Sub("Give Weapon Set (no run)", tiers);

            var cls = me.fighterClass;
            var sets = am.weaponSetDatabase.weaponSets;

            foreach (var tier in new[] { WeaponSet.Tier.Legendary, WeaponSet.Tier.Rare, WeaponSet.Tier.Common })
            {
                var kids = new List<MenuItem>();
                for (int i = 0; i < sets.Count; i++)
                {
                    var ws = sets.get_Item(i);
                    if (ws == null || ws.fighterClass != cls || ws.tier != tier) continue;

                    var set = ws;
                    var name = string.IsNullOrEmpty(ws.setName) ? ws.type.ToString() : ws.setName;
                    kids.Add(Fixed(name, () =>
                    {
                        GM.instance?.GivePlayerWeaponSet(set);
                        Cheats.ApplyWeaponTweaks();
                    }));
                }
                if (kids.Count > 0) tiers.Add(Sub($"{tier} ({kids.Count})", kids));
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"weapon picker: {e}"); }

        return Sub("Give Weapon Set", tiers);
    }
}
