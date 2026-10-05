# Ironstrike Trainer

> ## Status
>
> The cheat flags work. The **dev menu does not** — `GM.CreateDevMenu()` is deliberately refused by
> the game, and reaching for it shows a message from the developer and ends your run. A custom UI is
> in progress; until then v0.1.1 loads and applies flags but has no way to open a menu.
>
> Verified in game: with every `Cheat*` flag on and the dev menu untouched, the game's own guard
> stays off (`guard=False`). Nothing is uploaded and no account flag is set.

A cheat menu for [IRONSTRIKE](https://store.steampowered.com/app/3233230/IRONSTRIKE/), built as a
BepInEx 6 IL2CPP plugin.

The game ships with its developer menu still in it. This unlocks that menu and adds a Trainer
submenu to it, navigable from the right controller in VR.

Single-player only. See [Scope](#scope).

## Install

### 1. Get BepInEx — this exact file

**[⬇ BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip)**

Clicking that downloads it directly. For reference:

```
file    BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip
size    34,336,405 bytes  (32.7 MiB)
sha256  f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a
```

Verify it if you like — PowerShell: `Get-FileHash .\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip`,
or Linux/macOS: `sha256sum BepInEx-*.zip`.

That build page lists **13** files and only this one works. Do not take:

| Wrong file | Why |
| --- | --- |
| `BepInEx-Unity.Mono-win-x64-...` | Mono, not IL2CPP. Nearly the same name, no `dotnet/` folder, will not load. |
| `BepInEx-Unity.IL2CPP-win-x86-...` | 32-bit. The game is x64. |
| `BepInEx-Unity.IL2CPP-linux-x64-...` | For native Linux builds. IRONSTRIKE is a Windows binary even under Proton. |
| `BepInEx-NET.Framework-...` / `BepInEx-NET.CoreCLR-...` | Not Unity loaders at all. |

### 2. Extract it into the game folder

Open the folder containing `Ironstrike.exe` — in Steam: right-click IRONSTRIKE → Manage → Browse
local files.

**All six top-level items from the zip go directly in that folder**, not in a subfolder:

```
IRONSTRIKE/
├── Ironstrike.exe          ← already there
├── winhttp.dll             ← from the zip
├── doorstop_config.ini     ← from the zip
├── .doorstop_version       ← from the zip
├── changelog.txt           ← from the zip
├── dotnet/                 ← from the zip
└── BepInEx/                ← from the zip
```

This is the single most common mistake: the zip often extracts into a nested folder and people move
only `BepInEx/` across. Without `winhttp.dll` and `dotnet/` beside the exe, **nothing happens at all
on launch** — no new folders, no log.

### 3. Launch once

First run downloads Unity base libraries and generates interop assemblies. It takes about a minute
and the window may look frozen. Do not kill it. When it finishes you will have
`BepInEx/LogOutput.log` and a populated `BepInEx/interop/`.

On **Linux/Proton** you also need this in Steam → Properties → Launch Options, or Doorstop will not
inject (Wine prefers its own `winhttp`):

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

Not needed on Windows.

### 4. Turn off the console before using the headset

In `BepInEx/config/BepInEx.cfg`:

```ini
[Logging.Console]
Enabled = false
```

A console window stealing focus mid-session will wreck a run. Read `BepInEx/LogOutput.log` instead.

### 5. Add the mod

Drop `IronstrikeTrainer.dll` from the [latest release](../../releases) into `BepInEx/plugins/`.
Launch, then edit `BepInEx/config/eu.euroswarms.ironstrike.trainer.cfg` and relaunch.

### If nothing happens

If no `BepInEx/LogOutput.log` or `BepInEx/interop` appears after launching, Doorstop never
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

### The developer asked for exactly one thing

There is a string in the game binary addressed to modders. Verbatim:

> "Please don't offer cheats that can transfer to public multiplayer games. It ruins the challenge
> for people who don't want cheats on their team. I'm a solo indie developer (and busy taking care
> of a new baby), and I know I can't win an arms race with modders, so I appeal to you personally.
> Please limit cheats to private games with you and your friends."

So private games are fine and public lobbies are not. That is the whole rule, and this mod is built
around it rather than around it being unenforceable.

The trainer works in **Solo** and in **Private Match** (code) sessions, and nowhere else. While
it is loaded, the main menu's public entry points are greyed out and relabelled
`LOCKED (MODDED)`:

| Button | Method | |
| --- | --- | --- |
| Play (and the Archon-run variants) | `MainMenuUI.PressPlay` | quick match, public — **locked** |
| HOST | `MainMenuUI.PressHost` | hosts a game strangers can join — **locked** |
| Private Match | `MainMenuUI.PressPrivateMatch` | friends with a code — allowed |
| Solo | `MainMenuUI.PressSolo` | offline — allowed |

The matchmaking methods behind the public buttons (`StartMatchmaking`, `FindOrHostMatchmaking`)
are blocked too, in case anything else reaches them. Incoming connections are refused unless you
entered a private match, so friends can join a private game you host and nobody can join anything
else. The join code is never written to the log.

`SafeMode` controls the lock and `AllowPrivateMatches` controls the private-match exception; both
default to on.

It matters because the netcode cannot defend itself. Fusion Host mode makes one player's client
authoritative for everyone, and of roughly 45 RPCs only 7 are authority-gated; damage magnitude is
a caller-supplied float and player health rides in each client's own `NetworkInput`. Nothing would
stop a modded client from affecting other people, so the restraint is in this code.

### Not a currency mod

Nothing here grants Shards, Geodes, Essence, Animus or Gems, and it does not touch cosmetic prices.
That is a deliberate line, not a technical limit.

(An earlier version of this README claimed IRONSTRIKE was free-to-play with real-money IAP. That was
wrong — the Steam release is a paid game. The Gem IAP symbols and the `f2pVersion` flag belong to the
Quest/Meta build.)

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
