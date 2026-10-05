# Ironstrike Trainer

A trainer for [IRONSTRIKE](https://store.steampowered.com/app/3233230/IRONSTRIKE/), built as a
BepInEx 6 IL2CPP plugin. It adds a **TRAINER** menu to the game, next to Options, with god mode,
insta-kill, invisibility, movement and projectile multipliers, enemy friendly fire and bot
controls.

It works in **Solo** and **Private Match** games only. While it is loaded, public matchmaking is
locked. See [Multiplayer](#multiplayer).

## Install

### 1. Get BepInEx — this exact file

**[⬇ BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip)**

```
file    BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip
size    34,336,405 bytes  (32.7 MiB)
sha256  f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a
```

Verify it with `Get-FileHash` (PowerShell) or `sha256sum` (Linux/macOS).

The BepInEx build page lists 13 files and only this one works. Do not take:

| Wrong file | Why |
| --- | --- |
| `BepInEx-Unity.Mono-win-x64-...` | Mono, not IL2CPP. Nearly the same name, no `dotnet/` folder, will not load. |
| `BepInEx-Unity.IL2CPP-win-x86-...` | 32-bit. The game is x64. |
| `BepInEx-Unity.IL2CPP-linux-x64-...` | For native Linux builds. IRONSTRIKE is a Windows binary even under Proton. |
| `BepInEx-NET.Framework-...` / `BepInEx-NET.CoreCLR-...` | Not Unity loaders at all. |

### 2. Extract it into the game folder

Open the folder containing `Ironstrike.exe` (Steam: right-click IRONSTRIKE → Manage → Browse local
files). **All six top-level items from the zip go directly in that folder**, not in a subfolder:

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

The most common mistake is the zip extracting into a nested folder and only `BepInEx/` being moved.
Without `winhttp.dll` and `dotnet/` beside the exe, nothing happens on launch — no new folders, no
log.

### 3. Launch once

The first launch downloads Unity base libraries and generates interop assemblies. It takes about a
minute and the window may look frozen; do not close it. Afterwards you will have
`BepInEx/LogOutput.log` and a populated `BepInEx/interop/`.

**Linux/Proton only:** add this to Steam → Properties → Launch Options, or BepInEx will not load
(Wine prefers its own `winhttp`). If you already have launch options, put it before `%command%`
alongside them:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

Use Proton - Experimental. Nothing is needed on Windows.

### 4. Turn off the console before using a headset

In `BepInEx/config/BepInEx.cfg`:

```ini
[Logging.Console]
Enabled = false
```

A console window that steals focus mid-session will wreck a VR run. The log is in
`BepInEx/LogOutput.log`.

### 5. Add the trainer

Download the zip from [Releases](../../releases) and extract its `BepInEx/` folder over the game
folder, or put `IronstrikeTrainer.dll` into `BepInEx/plugins/` yourself. Mod managers that read
Thunderstore-style packages (r2modman, Gale) can install the zip directly.

### If nothing happens

If no `BepInEx/LogOutput.log` or `BepInEx/interop` appears after launching, BepInEx never loaded.
On Windows, run the included doctor with the game closed:

```powershell
.\bepinex-doctor.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\IRONSTRIKE"
```

It checks the usual causes: wrong BepInEx variant, zip extracted one folder too deep, missing
`winhttp.dll`, antivirus removing it, and Windows blocking downloaded files.

## Using it

Open the trainer with the small **TRAINER** button in the corner of the Options card on the main menu, or press **F1**
anywhere. It appears in front of you, in the game's own menu style.

```
SURVIVAL     ☐ God Mode   ☐ Invisible   ☐ Fast Regen   [ REVIVE ME ]
OFFENSE      ☐ Insta-Kill   ☐ High Damage   ☐ Low Cooldowns   ☐ All Ironstrikes
             [−] 1x [+]  Ironstrike Rate
MOVEMENT     [−] 1x [+]  Move Speed      [−] 1x [+]  Jump Height
WEAPONS      [−] 1x [+]  Melee Reach / Projectile Speed / Projectile Range
TEAM KILL    ☐ Enemies hurt each other   ☐ Players hurt each other (co-op, not yet)
VISUAL       ☐ Hide Healthbars   ☐ Hide Damage Numbers   ☐ Hide Status Effects
BOTS         [ HURT ALL BOTS ]  [ DESPAWN ALL BOTS ]  [ SPAWN DUMMY ]
RESET        [ RESET ALL TO VANILLA ]
```

- **Checkboxes** switch something on or off.
- **[−] value [+]** adjusts a multiplier: 0.25×, 0.5×, 0.75×, 1× (normal), 1.25×, 1.5×, 2×, 3×, 5×,
  10×.
- **Wide buttons** do something once.

Every setting is saved to `BepInEx/config/eu.euroswarms.ironstrike.trainer.cfg`, which can also be
edited directly.

Hotkeys: **F1** opens the trainer, **F2** re-applies the config, **F3** revives you, **F4** hurts all
bots, **F5** despawns all bots.

## How it works

The game's central object is a MonoBehaviour called `GM`. Its developer cheat flags (high damage,
fast regen, low cooldowns, all ironstrikes, HUD toggles) are public static fields, so those are set
directly.

Most of the other cheats go through one hook. `Fighter.CalcSkillAndStatusEffectValue` is the game's
central stat query — everything the skill and status system can modify passes through it, keyed by
`SkillCalcType` — so one postfix covers movement, jumping and weakspot range.

| Cheat | How |
| --- | --- |
| God Mode | keeps `Fighter.invulnerable` set; the game clears it on respawn |
| Insta-Kill | `Fighter.CalculateDamage`, only for damage you deal |
| Invisible | applies the game's own `StatusType.Invisibility` status effect |
| Move Speed, Jump | `SkillCalcType.MoveSpeed`, `JumpVelocity`, `JumpHorizontal` |
| Ironstrike Rate | `SkillCalcType.WeakspotRange` and `Weapon.WeakspotBaseInterval` |
| Melee Reach | scales the weapon; reach is collider geometry, so the weapon also looks bigger |
| Projectile Speed / Range | adjusts projectiles as they spawn; homing projectiles only get a longer lifetime, because the game steers them |
| Enemies hurt each other | splits the bots between two factions, so hits across the halves count as real damage |
| Despawn All Bots | the game's own `NetworkGameMaster.DespawnBot` for every bot |

Notes:

- The game's stat values could be multipliers or additive bonuses — compiled IL2CPP code does not
  say which. The trainer multiplies and also floors the result at `factor − 1`, so every knob has
  an effect either way.
- *Enemies hurt each other* uses whichever faction the game itself treats as hostile to both
  sides, so you can still hit every bot. Bots on the same half still do not hurt each other.
- *Players hurt each other* needs other players and is not implemented yet.

## Multiplayer

The game's developer left a message to modders in the game's code. Verbatim:

> "Please don't offer cheats that can transfer to public multiplayer games. It ruins the challenge
> for people who don't want cheats on their team. I'm a solo indie developer (and busy taking care
> of a new baby), and I know I can't win an arms race with modders, so I appeal to you personally.
> Please limit cheats to private games with you and your friends."

This trainer follows that. It works in **Solo** and in **Private Match** (join code) games, and
nowhere else. While it is loaded, the main menu's public play options are greyed out and read
`LOCKED (MODDED)`:

| Button | What it does | With the trainer |
| --- | --- | --- |
| Play (and the Archon Run variants) | quick match, public | locked |
| HOST | hosts a game strangers can join | locked |
| Private Match | play with friends using a code | allowed |
| Solo | offline | allowed |

The matchmaking functions behind the public buttons are blocked as well, so the only multiplayer
game you can host or join while modded is a Private Match. The join code is never written to the
log.

Two config settings control this, both on by default: `SafeMode` (the lock) and
`AllowPrivateMatches` (the private-match exception).

This matters because the game's netcode can't protect other players by itself: one player's game
runs the match for everyone, and most network messages are trusted without checks.

The trainer never touches currency — no Shards, Geodes, Essence, Animus or Gems — and it does not
change cosmetic prices.

## Building from source

Needs the .NET SDK 6.0 or newer. Reference assemblies are included in `refs/`, so a fresh clone
builds without a copy of the game.

```powershell
.\build.ps1                                                   # Windows
.\build.ps1 -GameDir "C:\...\steamapps\common\IRONSTRIKE"      # build and install into the game
```

```bash
./build.sh                                                     # Linux / macOS
```

After a game update, `./regen-interop.sh` rebuilds `refs/` from your own copy of the game. Or pass
`-p:InteropDir=<path to BepInEx/interop>` to build against the assemblies BepInEx generated.

Technical notes: Unity 2021.3.28f1, IL2CPP metadata v29 (unencrypted). Gameplay types live in the
IL2CPP image named `GameAssembly`, not `Assembly-CSharp`. There are no Addressables or asset
bundles. Burst-compiled code cannot be patched with Harmony.

## License

AGPL-3.0, covering this mod's source.

`refs/` holds machine-generated IL2CPP interop assemblies: type and member signatures with interop
stubs, no game logic. They are included only so the project builds without the game, and can be
regenerated from your own install with `regen-interop.sh`. No game assets are redistributed.

## Credits

IRONSTRIKE is by E McNeill. The cheat flags and the debug systems this trainer builds on are his,
shipped in the game.
