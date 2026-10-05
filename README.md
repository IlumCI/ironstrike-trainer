# Ironstrike Trainer

A cheat menu for [IRONSTRIKE](https://store.steampowered.com/app/3233230/IRONSTRIKE/), built as a
BepInEx 6 IL2CPP plugin.

The game ships with its developer menu still in it. This unlocks that menu and adds a Trainer
submenu to it, navigable from the right controller in VR.

Single-player only. See [Scope](#scope).

## Install

Grab the zip from [Releases](../../releases) and extract `BepInEx/` over your game folder, or
install it through r2modman / Gale.

You need BepInEx 6 IL2CPP first — `BepInExPack_IL2CPP` 6.0.755, or a bleeding-edge build from
[builds.bepinex.dev](https://builds.bepinex.dev/projects/bepinex_be). Get the
**Unity.IL2CPP win-x64** variant; the Mono one will not load.

Then, before you put the headset on, open `BepInEx/config/BepInEx.cfg` and set:

```ini
[Logging.Console]
Enabled = false
```

The console window steals focus mid-session and will wreck a run. Read `BepInEx/LogOutput.txt`
instead.

Press **F1** in game to open the dev menu, then pick Trainer.

### If nothing happens

If no `BepInEx/LogOutput.txt` or `BepInEx/interop` appears after launching, Doorstop never
injected. Run the included doctor with the game closed:

```powershell
.\bepinex-doctor.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\IRONSTRIKE"
```

It checks the usual culprits: wrong BepInEx variant, zip extracted one folder too deep, missing
`winhttp.dll`, antivirus eating Doorstop, and Mark-of-the-Web blocking the DLL.

## The menu

```
Trainer
├── Survival      God Mode · Invisible · Fast Regen · Revive Me
├── Offense       Insta-Kill · High Damage · Low Cooldowns
│                 All Ironstrikes · Ironstrike Rate
├── Movement      Move Speed · Jump Height
├── Weapons       Melee Reach · Projectile Speed · Projectile Range
├── Team Kill     Enemies hurt each other · Player side hurts each other
├── Visual        Hide healthbars / damage numbers / status effects
├── Bots          Hurt All · Despawn All · Spawn Dummy
├── Give Skill...        every skill for your class, at max level
├── Give Weapon Set...   Legendary / Rare / Common, filtered to your class
└── Reset All
```

`[x]` and `[ ]` are toggles. Values show as `< 2x >` and cycle
`1 → 1.25 → 1.5 → 2 → 3 → 5 → 10` each time you select them, since a menu entry only carries one
action.

F2 re-applies the config, F3 revives you, F4 and F5 hurt and despawn all bots. Hotkeys use legacy
`UnityEngine.Input`; if this build turns out to be Input-System-only the plugin logs a line and
turns them off, and everything stays reachable from the menu.

## How it works

The game's god-object is a MonoBehaviour called `GM`. Its eight `Cheat*` flags are `public static`
fields and the dev menu builders are public methods, so a lot of this mod is just calling code that
already exists.

Most of the value cheats go through one hook. `Fighter.CalcSkillAndStatusEffectValue` is the central
stat query — everything the skill and status system can modify passes through it keyed by
`SkillCalcType` — so a single postfix covers movement, jump, weakspot range and visibility.

| Cheat | How |
| --- | --- |
| God Mode | pins `Fighter.invulnerable`, which the game clears on respawn |
| Insta-Kill | `Fighter.CalculateDamage`, only when `hitInfo.attackingFighter` is you |
| Invisible | `CalcSkillAndStatusEffectFlag(Targetable)` returns false, the same lever Smoke Bombs and the Invisibility spell use |
| Move Speed, Jump | `SkillCalcType.MoveSpeed`, `JumpVelocity`, `JumpHorizontal` |
| Ironstrike Rate | `SkillCalcType.WeakspotRange` plus `Weapon.WeakspotBaseInterval`, the dev's own cadence field |
| Melee Reach | scales the weapon's `transform.localScale`; reach here is collider geometry, not a float, so a longer weapon genuinely reaches further |
| Projectiles | `Projectile.SetTypes` postfix adjusting `speed`, `maxLifeTime` and `gravity` |
| Team Kill | `GM.isSameTeam` postfix, per faction pair, with `EnemyBots` and the player side as separate toggles |
| Give Skill | `SkillManager.GiveSkillToFighter`, filtered by `Fighter.fighterClass` |
| Give Weapon Set | `GM.GivePlayerWeaponSet` over `ArmoryManager.weaponSetDatabase`, grouped by tier |

Two things to know. IL2CPP strips method bodies, so there is no way to tell from the binary whether
the game reads those stats as multipliers or as additive bonus percentages; the code multiplies and
also floors the result at `factor - 1` so the knob works either way. And Team Kill retargets the AI,
because `isSameTeam` is what the AI uses to choose targets — enemies will actively fight each other
rather than only clipping each other by accident.

## Scope

No currency. Nothing here grants Shards, Geodes, Essence, Animus or Gems, and it does not touch
cosmetic prices. IRONSTRIKE is free-to-play with real-money Gem IAP from a solo developer, and the
build persists `AnimusEverGainedSuspect` and `IsBanned` and fetches remote ban lists. The internals
are wide open and clearly not defended. The monetisation is the part that is.

Single-player only, enforced in code and on by default. The netcode is Fusion Host mode, so one
player's client is authoritative for everyone, and it validates almost nothing: of roughly 45 RPCs
only 7 are authority-gated, damage magnitude is a caller-supplied float, and player health rides in
each client's own `NetworkInput`. The game cannot stop a modded client from affecting other people,
so the check lives here. It is `NetworkLifecycle.SpawnedPlayerCount <= 1`, not `NetworkIsRunning()`,
because solo play still starts a Host session.

## Building

Needs the .NET SDK 6.0 or newer. Reference assemblies are committed in `refs/`, so a clean clone
compiles with no game files present.

Windows:

```powershell
.\build.ps1
.\build.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\IRONSTRIKE"   # build + install
.\build.ps1 -GameDir ... -Package                                                 # + make the zip
```

Linux or macOS:

```bash
./build.sh                    # build
./build.sh /mnt/ironstrike    # build + install over a mounted game dir
./package.sh                  # make the distributable zip
```

CI builds every push and attaches a zip to each `v*` tag.

If you own the game, `./regen-interop.sh` regenerates `refs/` from your own copy with Cpp2IL and
Il2CppInterop, which is worth doing after a game update. Pass `-p:InteropDir=<path to
BepInEx/interop>` to compile against the set the game generated at runtime instead.

Technically: Unity 2021.3.28f1, IL2CPP metadata v29 unencrypted, so stock tooling works with no
unpacking. The gameplay types live in the image named `GameAssembly.dll`, not `Assembly-CSharp.dll`.
There are no Addressables or asset bundles. Burst code in `lib_burst_generated.dll` has no IL2CPP
`MethodInfo` and cannot be patched with Harmony.

## License

AGPL-3.0, covering this mod's source.

`refs/` holds machine-generated IL2CPP interop assemblies. They are API surface — type and member
signatures with interop trampolines, no game logic — and are committed only so the project builds
without a copy of the game. No game assets are redistributed. Regenerate them yourself from your own
install with `regen-interop.sh` if you would rather not trust the committed ones.

## Credits

IRONSTRIKE is by E McNeill. The dev menu, the cheat flags, the AI debug visualisers and the in-game
tuner editor are all his, shipped in the retail build. This opens a door he left unlocked.

There is no published mod policy, so it is worth asking on the
[Discord](https://www.ironstrikegame.com/discord) before redistributing anything.
