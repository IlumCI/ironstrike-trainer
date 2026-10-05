A trainer for IRONSTRIKE that adds a **TRAINER** menu next to Options (or press **F1**): god mode,
insta-kill, invisibility, movement and projectile multipliers, enemy friendly fire, and bot
controls.

Works in **Solo** and **Private Match** games only. While it is loaded, Play and HOST (public
matchmaking) are locked.

### Install

1. Install **BepInEx 6 IL2CPP** — this exact file:
   [BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip).
   All six items in it go directly beside `Ironstrike.exe`. Launch the game once and let it finish
   setting up.
2. Extract this release's zip over the game folder (it contains `BepInEx/plugins/IronstrikeTrainer.dll`),
   or install it with r2modman / Gale.
3. Before using a headset, set `Enabled = false` under `[Logging.Console]` in
   `BepInEx/config/BepInEx.cfg`.

On Linux/Proton, add `WINEDLLOVERRIDES="winhttp=n,b"` to the game's launch options.

Full instructions and troubleshooting are in the README.
