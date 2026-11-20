# Battle Resupply

A mod for **Grand Tactician: The Civil War (1861-1865)** that lets regiments resupply their ammunition *during* the
battle day, not only at nightfall. It works for both sides, your army and the AI's.

## Why

In the base game, ammunition moves from an army's ammunition train into its regiments only at the end of each battle
day. During the day nothing refills, so units with high rate-of-fire weapons (breech-loaders, repeaters) and anyone in
a long firefight run dry hours before dark, while the train sits full behind them.

## What it does

Every few game minutes, each regiment that is short of ammunition and has been **quiet for 10 minutes** (not firing,
not in melee, not under fire) draws ammunition from its own train, up to a full load per game hour. It uses the game's
own trains and bookkeeping:

- infantry and cavalry draw small-arms loads, batteries draw artillery loads;
- the trains are finite, so a long fight can still exhaust them;
- the game's own nightly refill is unchanged.

So the tactic is the historical one: pull a regiment back out of the fight for a few minutes and it refills.

If a brigade's own train is empty (a train shrinks when its headquarters takes losses), its regiments draw on the
**nearest friendly train** that still has ammunition.

Your own regiments show *Resupplying ammunition* and *Ammunition replenished* as they refill.

## Install

1. **Install BepInEx 5** (the mod loader), if you don't have it already. Either:
   - use the full download from the Releases page, which includes BepInEx 5.4.23.5; or
   - download **BepInEx_win_x64_5.4.23.5.zip** from <https://github.com/BepInEx/BepInEx/releases> and unzip it into
     the game folder.

   The game folder is usually
   `C:\Program Files (x86)\Steam\steamapps\common\Grand Tactician The Civil War (1861-1865)`
   (in Steam: right-click the game > Manage > Browse local files).
   After unzipping, the game folder holds `winhttp.dll`, `doorstop_config.ini` and a `BepInEx` folder.
2. **Run the game once** and close it. BepInEx creates its folders (`BepInEx\plugins`, `BepInEx\config`).
3. **Copy** `BattleResupply.dll` into `BepInEx\plugins\BattleResupply\` (create the folder).
4. Start the game. `BepInEx\LogOutput.log` should contain `Battle Resupply ready`.

The plugin-only download is just step 3. The full download already has everything in place: unzip it into the game
folder and start the game.

**Note:** Steam's *Verify integrity of game files* doesn't remove BepInEx or the mod, but a game update could change
the code the mod hooks into. If that happens the mod writes an error to `BepInEx\LogOutput.log` and does nothing; it
doesn't break your game or saves.

## Uninstall

Delete `BepInEx\plugins\BattleResupply\`. To remove BepInEx too, delete `winhttp.dll`, `doorstop_config.ini`,
`.doorstop_version` and the `BepInEx` folder. Saves are unaffected either way: the mod stores nothing in them.

## Settings

After the first start with the mod, `BepInEx\config\gtcw.battleresupply.cfg` holds:

| Setting | Default | What it does |
|---|---|---|
| `RefillPerHour` | 1 | Share of a full load a quiet regiment can draw per game hour. |
| `QuietMinutes` | 10 | Minutes without firing, melee or receiving fire before a regiment can refill. |
| `StepMinutes` | 5 | How often (game minutes) resupply is applied. |
| `ShowText` | true | Floating text over your own regiments as they refill. |
| `CommanderRange` | 0 | If above 0, a regiment must be within this distance (map units) of its brigade headquarters. 0 = off. |
| `BorrowTrains` | true | A regiment whose own train is empty draws on the nearest friendly train. |
| `BorrowRange` | 0 | If above 0, a borrowed train must be within this distance. 0 = any distance. |
| `Hourly` (Log) | true | Each game hour, write both armies' ammunition state and your trains to the log. |

## The log

With `Hourly` on, `BepInEx\LogOutput.log` shows each game hour, for example:

```
Your army at 05:00: 34 regiments short of ammunition, 0 of them quiet; 5 refills this hour (382 men-loads); ammunition train left: 25009 men-loads.
Your trains at 05:00:
    1st Kentucky Inf. Bgd.: train 3900 men-loads; 5 short, 0 quiet, 0 refilling, 3 firing, 2 under fire
```

so you can see whether a brigade isn't refilling because of its train or because its regiments are still in the fight.

## Building from source

Requires the game and BepInEx installed. From Git Bash:

```
sh build.sh
```

The script uses the C# compiler that ships with Windows (.NET Framework 4) and references the game's own libraries;
set `GAME` if your game folder is elsewhere.

## License

MIT, see [LICENSE](LICENSE). Copyright (c) 2026 Drew McGunn.

BepInEx (included in the full download) is LGPL-2.1, <https://github.com/BepInEx/BepInEx>; it bundles HarmonyX,
MonoMod and Mono.Cecil (MIT). See THIRD-PARTY-NOTICES.txt.
