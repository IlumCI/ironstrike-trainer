**Verified loading in game.** BepInEx injects, the Harmony hook on `GM.Update` fires, and the cheat
flags are confirmed written and read back from the live game. Opening the menu with F1 and the
skill/weapon pickers are still unconfirmed by hand.

Drop-in install: download the zip and extract `BepInEx/` over your IRONSTRIKE folder
(the one containing `Ironstrike.exe`), or install it with r2modman / Gale.

Requires BepInEx 6 IL2CPP (`BepInExPack_IL2CPP` 6.0.755 or a newer bleeding-edge build).
Before putting the headset on, set `Enabled = false` under `[Logging.Console]` in
`BepInEx/config/BepInEx.cfg` — a console window stealing focus mid-session will wreck a run.

Open the dev menu with F1, then go to Trainer. Single-player only.
