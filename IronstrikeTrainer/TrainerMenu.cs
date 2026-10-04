using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Assets.Scripts.Utilities;

namespace IronstrikeTrainer;

/// <summary>
/// Builds a "Trainer" submenu and grafts it onto the dev menu's own MenuItem tree.
///
/// This deliberately reuses <c>ILDevMenuManager</c> rather than drawing a new UI: it is already a
/// VR-native text menu driven by the right controller, with the dev's own debounce and anchoring.
/// We get controller navigation for free and write no canvas code.
///
/// Rendering convention:
///   toggles -> "[x] Name" / "[ ] Name"
///   values  -> "Name  &lt; 2.0x &gt;"   (Select cycles through Cheats.Steps)
/// </summary>
internal static class TrainerMenu
{
    /// <summary>
    /// Il2Cpp delegates must outlive the managed lambda, otherwise the GC collects the trampoline
    /// and the menu action crashes the game. Keeping strong refs here is mandatory, not tidiness.
    /// </summary>
    private static readonly List<object> _keepAlive = new();

    /// <summary>Label refreshers, run after any action so the menu text reflects new state.</summary>
    private static readonly List<Action> _refreshers = new();

    private static Il2CppSystem.Action Act(Action fn)
    {
        var del = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() =>
        {
            try
            {
                fn();
                Refresh();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Menu action failed: {e}");
            }
        }));
        _keepAlive.Add(del);
        return del;
    }

    private static MenuItem Leaf(Func<string> label, Action onSelect)
    {
        var item = new MenuItem(label(), Act(onSelect));
        _refreshers.Add(() => { try { item.text = label(); } catch { } });
        _keepAlive.Add(item);
        return item;
    }

    /// <summary>
    /// Leaf with a fixed label. Picker entries must use this: they are rebuilt every time the
    /// submenu opens, and registering a refresher per entry would grow _refreshers without bound,
    /// making every subsequent menu action slower.
    /// </summary>
    private static MenuItem LeafStatic(string label, Action onSelect)
    {
        var item = new MenuItem(label, Act(onSelect));
        _keepAlive.Add(item);
        return item;
    }

    private static MenuItem Node(string text, List<MenuItem> children)
    {
        var arr = new Il2CppReferenceArray<MenuItem>(children.Count);
        for (int i = 0; i < children.Count; i++) arr[i] = children[i];
        var item = new MenuItem(text, arr);
        _keepAlive.Add(item);
        return item;
    }

    private static void Refresh()
    {
        foreach (var r in _refreshers) r();
        try
        {
            var dm = GM.instance?.ILDevMenuManager;
            if (dm != null) { dm.needsUpdate = true; dm.UpdateMainText(); }
        }
        catch { }
    }

    private static string Tick(bool on) => on ? "[x] " : "[ ] ";
    private static string Mult(float v) => $"< {v:0.##}x >";

    // ======================================================================

    public static MenuItem Build()
    {
        _keepAlive.Clear();
        _refreshers.Clear();

        var c = Plugin.C;

        var survival = new List<MenuItem>
        {
            Leaf(() => Tick(Cheats.GodMode)  + "God Mode",  () => Cheats.GodMode  = !Cheats.GodMode),
            Leaf(() => Tick(Cheats.Invisible) + "Invisible (enemies ignore you)",
                 () => Cheats.Invisible = !Cheats.Invisible),
            Leaf(() => Tick(c.FastRegen.Value) + "Fast Regen",
                 () => Set(c.FastRegen, !c.FastRegen.Value)),
            Leaf(() => "Revive Me", () => GmHooks.ReviveLocal()),
        };

        var offense = new List<MenuItem>
        {
            Leaf(() => Tick(Cheats.InstaKill) + "Insta-Kill",
                 () => Cheats.InstaKill = !Cheats.InstaKill),
            Leaf(() => Tick(c.HighDamage.Value) + "High Damage (dev flag)",
                 () => Set(c.HighDamage, !c.HighDamage.Value)),
            Leaf(() => Tick(c.LowCooldowns.Value) + "Low Cooldowns (dev flag)",
                 () => Set(c.LowCooldowns, !c.LowCooldowns.Value)),
            Leaf(() => Tick(c.AllIronstrikes.Value) + "All Ironstrikes (dev flag)",
                 () => Set(c.AllIronstrikes, !c.AllIronstrikes.Value)),
            Leaf(() => "Ironstrike Rate  " + Mult(Cheats.IronstrikeRate), () =>
                 { Cheats.IronstrikeRate = Cheats.NextStep(Cheats.IronstrikeRate);
                   Cheats.ApplyWeaponTweaks(); }),
        };

        var teamkill = new List<MenuItem>
        {
            Leaf(() => Tick(Cheats.TeamKillEnemies) + "Enemies hurt each other",
                 () => Cheats.TeamKillEnemies = !Cheats.TeamKillEnemies),
            Leaf(() => Tick(Cheats.TeamKillPlayers) + "Player side hurts each other",
                 () => Cheats.TeamKillPlayers = !Cheats.TeamKillPlayers),
        };

        var movement = new List<MenuItem>
        {
            Leaf(() => "Move Speed   " + Mult(Cheats.MoveSpeed),
                 () => Cheats.MoveSpeed = Cheats.NextStep(Cheats.MoveSpeed)),
            Leaf(() => "Jump Height  " + Mult(Cheats.JumpHeight),
                 () => Cheats.JumpHeight = Cheats.NextStep(Cheats.JumpHeight)),
        };

        var weapons = new List<MenuItem>
        {
            Leaf(() => "Melee Reach  " + Mult(Cheats.MeleeReach), () =>
                 { Cheats.MeleeReach = Cheats.NextStep(Cheats.MeleeReach);
                   Cheats.ApplyWeaponTweaks(); }),
            Leaf(() => "Projectile Speed  " + Mult(Cheats.ProjectileSpeed),
                 () => Cheats.ProjectileSpeed = Cheats.NextStep(Cheats.ProjectileSpeed)),
            Leaf(() => "Projectile Range  " + Mult(Cheats.ProjectileRange),
                 () => Cheats.ProjectileRange = Cheats.NextStep(Cheats.ProjectileRange)),
        };

        var visual = new List<MenuItem>
        {
            Leaf(() => Tick(c.NoHealthbars.Value) + "Hide Healthbars",
                 () => Set(c.NoHealthbars, !c.NoHealthbars.Value)),
            Leaf(() => Tick(c.NoDamageNumbers.Value) + "Hide Damage Numbers",
                 () => Set(c.NoDamageNumbers, !c.NoDamageNumbers.Value)),
            Leaf(() => Tick(c.NoStatusEffects.Value) + "Hide Status Effects",
                 () => Set(c.NoStatusEffects, !c.NoStatusEffects.Value)),
        };

        var bots = new List<MenuItem>
        {
            Leaf(() => "Hurt All Bots",    () => GmHooks.Bots(hurt: true)),
            Leaf(() => "Despawn All Bots", () => GmHooks.Bots(hurt: false)),
            Leaf(() => "Spawn Dummy",      () => { GM.instance?.SpawnDummyPlayer(); }),
        };

        return Node("Trainer", new List<MenuItem>
        {
            Node("Survival", survival),
            Node("Offense",  offense),
            Node("Movement", movement),
            Node("Weapons",  weapons),
            Node("Team Kill", teamkill),
            Node("Visual",   visual),
            Node("Bots",     bots),
            SkillPicker.Build(),
            WeaponPicker.Build(),
            Leaf(() => "Reset All", ResetAll),
        });
    }

    private static void Set(BepInEx.Configuration.ConfigEntry<bool> e, bool v)
    {
        e.Value = v;
        GmHooks.Apply("menu");
    }

    private static void ResetAll()
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
        Plugin.Log.LogInfo("Trainer reset to vanilla.");
    }

    // ------------------------------------------------------------------
    //  Pickers — filtered to the local fighter's class
    // ------------------------------------------------------------------

    private static class SkillPicker
    {
        public static MenuItem Build()
        {
            // Rebuilt on open: fighterClass is unknown until a run starts.
            var f = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<MenuItem>>(
                new Func<MenuItem>(BuildNow));
            _keepAlive.Add(f);
            var item = new MenuItem("Give Skill...", f);
            _keepAlive.Add(item);
            return item;
        }

        private static MenuItem BuildNow()
        {
            var kids = new List<MenuItem>();
            try
            {
                var sm = SkillManager.instance;
                var me = Cheats.Local;
                if (sm == null || me == null) return Node("Give Skill (no run active)", kids);

                var cls = me.fighterClass;
                var all = sm.GetSkills();
                for (int i = 0; i < all.Count; i++)
                {
                    var sk = all.get_Item(i);
                    if (sk == null || sk.hidden || sk.skillClass != cls) continue;

                    var type = sk.skillType;
                    var name = sk.skillName;
                    var lvl = sk.allowsEnhanced ? 10 : 5;   // max, enhanced tier where allowed
                    kids.Add(LeafStatic($"{name} (L{lvl})",
                                        () => sm.GiveSkillToFighter(type, Cheats.Local, lvl)));
                }
                Plugin.Log.LogInfo($"Skill picker: {kids.Count} skills for class {cls}.");
            }
            catch (Exception e) { Plugin.Log.LogError($"Skill picker failed: {e}"); }

            return Node($"Give Skill ({kids.Count})", kids);
        }
    }

    private static class WeaponPicker
    {
        public static MenuItem Build()
        {
            var f = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<MenuItem>>(
                new Func<MenuItem>(BuildNow));
            _keepAlive.Add(f);
            var item = new MenuItem("Give Weapon Set...", f);
            _keepAlive.Add(item);
            return item;
        }

        private static MenuItem BuildNow()
        {
            var tiers = new List<MenuItem>();
            try
            {
                var am = ArmoryManager.instance;
                var me = Cheats.Local;
                if (am?.weaponSetDatabase == null || me == null)
                    return Node("Give Weapon Set (no run active)", tiers);

                var cls = me.fighterClass;
                var sets = am.weaponSetDatabase.weaponSets;

                // Group by tier so "upgraded" weapons (Rare / Legendary) are one hop away.
                foreach (var tier in new[] { WeaponSet.Tier.Legendary, WeaponSet.Tier.Rare,
                                             WeaponSet.Tier.Common })
                {
                    var kids = new List<MenuItem>();
                    for (int i = 0; i < sets.Count; i++)
                    {
                        var ws = sets.get_Item(i);
                        if (ws == null || ws.fighterClass != cls || ws.tier != tier) continue;
                        var captured = ws;
                        var nm = string.IsNullOrEmpty(ws.setName) ? ws.type.ToString() : ws.setName;
                        kids.Add(LeafStatic(nm,
                                            () => { GM.instance?.GivePlayerWeaponSet(captured);
                                                    Cheats.ApplyWeaponTweaks(); }));
                    }
                    if (kids.Count > 0) tiers.Add(Node($"{tier} ({kids.Count})", kids));
                }
                Plugin.Log.LogInfo($"Weapon picker: {tiers.Count} tiers for class {cls}.");
            }
            catch (Exception e) { Plugin.Log.LogError($"Weapon picker failed: {e}"); }

            return Node("Give Weapon Set", tiers);
        }
    }
}
