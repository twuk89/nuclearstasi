# Nuclearstasi

An Antistasi-style campaign mode for *Nuclear Option*: start with one airstrip against an occupier that owns the map, chip away at it, and grow.

**Status:** Plan 1 (foundation) — territory income, aggression meter, occupier reinforcements, campaign saving, `/ns` command. The enemy commander, captured production, nuke convoys and capital victory come in later updates.

## Install (host only)

1. Install BepInEx 5 (x64) into the Nuclear Option folder and run the game once.
2. Copy the `Nuclearstasi` folder from `Nuclearstasi.zip` into `BepInEx/plugins/`.
3. Start the game. `BepInEx/LogOutput.log` should contain `Nuclearstasi 0.1.0 loaded`.

Friends joining your game do not need the mod.

## Mission setup

Build the mission in the vanilla mission editor on Heartland:

- Name it starting with `Nuclearstasi` (e.g. `Nuclearstasi - Heartland`).
- Two factions. Rebels: players can join. Occupiers: tick **prevent join**.
- Give the rebels one small airstrip; give every other airbase to the occupiers.
- Name the enemy capital airbase starting with `[CAPITAL]` and the nuclear depot starting with `[NUKE]`.
- Put factories and vehicle depots inside airbases (they change hands with the airbase).
- The occupiers' starting supply list is also their reinforcement baseline.

## Commands

| Command | Who | What |
|---|---|---|
| `/ns` or `/ns status` | everyone | aggression, tier, territory, funds, campaign time |
| `/ns save` | host | save now |
| `/ns aggro <0-100>` | host | set aggression (testing) |
| `/ns new` | host | wipe the save; restart the mission to begin again |

Settings: `BepInEx/config/com.nuclearstasi.mod.cfg`. Save: `BepInEx/config/Nuclearstasi/campaign.json`.

## In-game test checklist

Send `BepInEx/LogOutput.log` after each session.

1. Vanilla mission (not Nuclearstasi): plays normally; log shows no `Activated:` line.
2. Nuclearstasi mission: after ~3 s, HQ message "a new campaign has begun"; log shows `Activated: rebels=…, occupiers=…, sites=N`.
3. `/ns status` shows ~0 aggression, QUIET, territory 1/N (plus factories/depots in your airstrip).
4. Funds: personal funds rise every 30 s by about 5 split between players.
5. Kill an enemy aircraft → log `Aggression +AircraftKilled -> 2.0 QUIET`.
6. `/ns aggro 30` → HQ message "Occupation forces are now on ALERT".
7. Capture an enemy airbase → HQ message "Rebels have captured …", aggression +15 (+10 per factory/depot), income rises.
8. Wait 10 minutes → log `Occupier reinforcements: N units`.
9. Quit to menu, reload the mission → "campaign resumed"; captured airbase is still yours, aggression and funds restored.
10. Nuclear escalation messages never appear, and nuclear weapons are not offered in the loadout screen.
11. A friend without the mod can join and sees the HQ messages.
12. If the save belongs to a different mission, an HQ message says so and suggests `/ns new`; the campaign stays inactive.
13. On a resumed campaign the log has no `SPLIT N WARHEADS` line after `Activated:`; on a new campaign it may.
14. Quitting to menu logs `Campaign saved` before the mission unloads.

## Notes

- Playing the mission from the editor activates the mod and writes the real campaign save.
- `/ns status` "Rebel funds" shows the faction pool, not players' personal funds.
- Occupier units placed in the mission at airbases you have captured come back on reload and may contest them until garrisons arrive in a later update.

## Building

Needs .NET SDK 8. Put the game/BepInEx DLLs in `lib/` (git-ignored, never committed): `Assembly-CSharp.dll`, `Mirage.dll`, `UniTask.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll` (from `NuclearOption_Data/Managed`), `BepInEx.dll`, `0Harmony.dll` (from `BepInEx/core`).

```bash
dotnet test tests/Nuclearstasi.Core.Tests
./scripts/package.sh
```
