# IRONSTRIKE Trainer

A BepInEx 6 (IL2CPP) plugin for **IRONSTRIKE** (Steam appid 3233230) that unlocks the
developer menu and cheat flags the dev already shipped in the retail build.

As far as I can tell this is the **first mod for the game** — no Thunderstore community, no
GitHub results, no Nexus page.

> **Single-player only, by design.** See [Scope](#scope).

## What it does

IRONSTRIKE's god-object is a `MonoBehaviour` called `GM` (global namespace). It carries the dev's
own cheat flags as `public static` fields and the dev menu builders as public methods. The mod
reaches those, then grafts a **Trainer** submenu onto the dev menu's own `MenuItem` tree — so you
get controller-driven VR navigation for free and no new canvas code.

```
Dev Menu
└── Trainer
    ├── Survival      [x] God Mode · [x] Invisible · [x] Fast Regen · Revive Me
    ├── Offense       [x] Insta-Kill · [x] High Damage · [x] Low Cooldowns
    │                 [x] All Ironstrikes · Ironstrike Rate  < 2x >
    ├── Movement      Move Speed  < 2x > · Jump Height  < 2x >
    ├── Weapons       Melee Reach  < 2x > · Projectile Speed  < 2x > · Projectile Range  < 2x >
    ├── Team Kill     [x] Enemies hurt each other · [x] Player side hurts each other
    ├── Visual        [x] Hide Healthbars · [x] Hide Damage Numbers · [x] Hide Status Effects
    ├── Bots          Hurt All Bots · Despawn All Bots · Spawn Dummy
    ├── Give Skill...        every skill for your class, at max/enhanced level
    ├── Give Weapon Set...   grouped Legendary / Rare / Common, filtered to your class
    └── Reset All
```

`[x]` / `[ ]` are toggles. `< 2x >` cycles a multiplier through `1 → 1.25 → 1.5 → 2 → 3 → 5 → 10`
and wraps.

### How each cheat is implemented

Most value cheats route through a single hook: **`Fighter.CalcSkillAndStatusEffectValue`**, the
game's central stat query. Every stat the skill/status system can touch passes through it keyed by
`SkillCalcType`, so one postfix covers movement, jump, weakspot range and visibility at once.

| Cheat | Mechanism |
| --- | --- |
| God Mode | pins `Fighter.invulnerable` each frame (game clears it on respawn) |
| Insta-Kill | `Fighter.CalculateDamage` postfix, **only** when `hitInfo.attackingFighter` is you |
| Invisible | `CalcSkillAndStatusEffectFlag(Targetable)` → `false`, plus `VisibilityPercent` → 0 — the same lever Smoke Bombs and the Invisibility spell pull |
| Move Speed / Jump | `SkillCalcType.MoveSpeed` / `JumpVelocity` / `JumpHorizontal` |
| Ironstrike Rate | `SkillCalcType.WeakspotRange` **and** `Weapon.WeakspotBaseInterval` (the dev's own cadence field — smaller interval = weakspots appear more often) |
| Melee Reach | scales the equipped weapon's `transform.localScale`; reach is collider geometry in this game, not a float, so a bigger weapon genuinely reaches further |
| Projectile Speed / Range | `Projectile.SetTypes` postfix → `speed`, `maxLifeTime`, `gravity`, owner-checked |
| Team Kill | `GM.isSameTeam` postfix, per faction pair — `EnemyBots` vs `EnemyBots` and `LocalPlayer`/`Allies` vs each other, toggled separately |
| Give Skill | `SkillManager.GiveSkillToFighter(type, fighter, level)`, filtered by `Fighter.fighterClass` |
| Give Weapon Set | `GM.GivePlayerWeaponSet(set)` over `ArmoryManager.weaponSetDatabase`, filtered by class and grouped by `Tier` |
| The eight `Cheat*` flags | set directly on `GM` — they are `public static` |

Two honest caveats:

- **Multiplier semantics are unverified.** IL2CPP strips method bodies, so whether the game reads
  these stats as multipliers or as additive bonus-percentages can't be read off the binary. The code
  multiplies *and* floors the result at `(factor - 1)` so the knob bites either way. If a stat feels
  far too strong or weak, adjust `Cheats.Steps`.
- **Team Kill also retargets AI.** `isSameTeam` is the same predicate the AI consults for target
  selection, so enemies will deliberately fight each other rather than only hurting each other by
  accident. That is usually the more fun version; narrowing it to accidental-only would mean moving
  the check down into the per-hit path.

### Hotkeys

Optional — they use legacy `UnityEngine.Input`, and the plugin detects and self-disables with a log
line if this build is Input-System-only. Everything is reachable from the VR menu regardless.

| Key | Action |
| --- | --- |
| `F1` | toggle the dev menu (with Trainer grafted on) |
| `F2` | re-apply config |
| `F3` | revive yourself |
| `F4` / `F5` | hurt / despawn all bots |

## Scope

The plugin **does not touch currency** — no Shards, Geodes, Essence, Animus or Gems, and it does
not edit cosmetic prices. IRONSTRIKE is free-to-play with real-money Gem IAP from a solo developer,
and the build persists `AnimusEverGainedSuspect` / `IsBanned` and fetches remote ban lists. The
internals are wide open and clearly not defended; the monetisation is the thing that is. Flipping
gameplay flags in solo play is in the spirit of what the dev left behind. Minting currency is not.

It also **gates itself to solo play** (`SoloOnly`, on by default). This is not caution theatre —
the netcode genuinely cannot defend itself:

- Topology is Fusion **Host mode**, so one player's client is authoritative for everyone.
- Of ~45 RPCs, only 7 are `StateAuthority`-gated. The rest are `RpcSources.All`, including
  `RPC_GiveSkillToPlayer`, `RPC_HealPlayer`, `RPC_SetStatusValue`, `RPC_TallyFighter`, and all four
  damage RPCs — where **damage magnitude is a caller-supplied float**.
- `PlayerNetworkInput` carries `CurrentHealth`/`MaxHealth`, which the host copies into authoritative
  networked state every tick, so player HP is effectively client-authoritative.
- There is no damage/heal/skill validation, no bounds, no rate limiting. Moderation is a
  plaintext-HTTP name ban list plus a cooperative vote-kick.

Since the game won't stop a modded client from affecting other people, the restraint lives here.
The solo check is `NetworkLifecycle.SpawnedPlayerCount <= 1` — note `NetworkIsRunning()` is *not* a
solo test, because solo play still starts a Fusion Host session.

## Building (Linux)

Needs only `dotnet-sdk`. No Wine, no Windows, no running game.

```bash
./regen-interop.sh          # Cpp2IL + Il2CppInterop -> ./interop  (compile references)
./build.sh                  # -> IronstrikeTrainer/bin/Release/net6.0/IronstrikeTrainer.dll
./build.sh /mnt/ironstrike  # build + deploy to an SMB-mounted game dir
```

`regen-interop.sh` generates interop assemblies locally from `GameAssembly.dll` +
`global-metadata.dat`, so the plugin can be written and compiled against real game types without
ever launching the game. Re-run it after any game update.

Prefer the game's own `BepInEx/interop` when it exists — `build.sh` picks it automatically if you
pass the game dir, since those are what the runtime actually generated.

## Installing (Windows)

The game runs on Windows; Linux is just the dev box.

1. Download **BepInEx 6 IL2CPP win-x64** `6.0.0-be.788` from
   <https://builds.bepinex.dev/projects/bepinex_be> and extract it into the game root, beside
   `Ironstrike.exe`.
2. Launch once. First run generates interop assemblies — **1–5 minutes, and the window may look
   frozen. Do not kill it.** Launch twice if nothing appears.
   Success looks like `BepInEx/interop/assembly-hash.txt` plus a populated `BepInEx/interop/`.
3. **Before putting the headset on**, edit `BepInEx/config/BepInEx.cfg`:
   ```ini
   [Logging.Console]
   Enabled = false
   ```
   A console window stealing focus mid-VR-session is the most likely way to wreck a run. Read
   `BepInEx/LogOutput.txt` instead.
4. Drop `IronstrikeTrainer.dll` into `BepInEx/plugins/`.
5. Launch, then edit `BepInEx/config/IronstrikeTrainer.cfg` and relaunch (or press `F2`).

### Verifying the hooks fired

Under IL2CPP a Harmony patch on an inlined method applies with no error and then silently never
runs, so the plugin logs explicit confirmation. Look for:

```
HOOK CONFIRMED: GM.InitScene postfix fired.
HOOK CONFIRMED: GM.Update postfix fired.
```

If those never appear, the patches aren't live and nothing else matters yet. `GM.InitScene` and
`GM.Update` are Unity message methods invoked from native code, so they should be safe targets.

## Technical notes

- Unity **2021.3.28f1**, IL2CPP, metadata **v29**, magic `0xFAB11BAF` — unencrypted and
  unobfuscated, so stock tooling works with no unpacking.
- The game's gameplay types live in the image named **`GameAssembly.dll`**, not
  `Assembly-CSharp.dll` (which holds third-party code). `GM` is there.
- No Addressables and no AssetBundles — content is classic serialized files loaded by build index.
- `Ironstrike_Data/StreamingAssets/` has loose, runtime-parsed CSVs (`skillvalues.csv`,
  `spellvalues.csv`, `CosmeticsData.csv`) and dialogue JSON. `SkillDatabase.fileHash` +
  `GetHash(fileName)` + `forceRead` is cache invalidation, not tamper detection, so edits should
  re-read. Untested as of v0.1.
- Burst code (`lib_burst_generated.dll`) has no IL2CPP `MethodInfo` and cannot be Harmony-patched.

## Shipping it

**There is no need to invent a mods folder — BepInEx already defines one.** Plugins live in
`BepInEx/plugins/`, the loader scans it at startup, and every tool in the ecosystem expects exactly
that. Adding a custom loader or directory on top would buy nothing.

`./package.sh` produces `dist/IronstrikeTrainer-<version>.zip`, laid out so one file serves both
install paths:

```
manifest.json                         <- Thunderstore / r2modman / Gale read this
icon.png                              <- 256x256, required by Thunderstore
README.md
BepInEx/plugins/IronstrikeTrainer.dll <- manual install: drag BepInEx/ onto the game folder
```

Distribution options, easiest first:

1. **GitHub Release** — attach the zip. Works today, no gatekeeping. Users drop the DLL in
   `BepInEx/plugins/`. This is the right first move.
2. **Thunderstore** — the standard for BepInEx mods and what gives one-click installs via r2modman
   and Gale. IRONSTRIKE has **no Thunderstore community yet**; you have to request one (via
   Thunderstore's Discord / GitHub) before you can publish. The zip is already in their format, so
   this is a paperwork step, not a code one.
3. **Nexus Mods** — no IRONSTRIKE game page either; also a request.

`manifest.json` declares a dependency on `BepInEx-BepInExPack_IL2CPP-6.0.755` so mod managers
install the loader automatically. Note BepInEx 6 IL2CPP has **never had a stable release**, so pin
a specific build rather than tracking latest.

Before publishing anywhere, ask on the [official Discord](https://www.ironstrikegame.com/discord) —
there is no published mod policy, and E McNeill is a solo dev.

## Credits

IRONSTRIKE is by **E McNeill**. The dev menu, cheat flags, AI debug visualisers and in-game VR
tuner editor in this mod are all *his* work, shipped in the retail build — this plugin only opens
the door he left unlocked.

There is no published mod policy. Worth asking on the
[official Discord](https://www.ironstrikegame.com/discord) before distributing anything.
