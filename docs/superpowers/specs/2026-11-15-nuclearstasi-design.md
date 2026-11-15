# Nuclearstasi — Design Spec

**Date:** 2026-11-15
**Status:** Approved in brainstorming, pending written-spec review

## 1. Summary

Nuclearstasi is an Antistasi-style campaign mode for *Nuclear Option*. Players
start as a tiny rebel faction holding one small airstrip on the Heartland map;
an occupying faction owns everything else. Players chip away at the occupier,
capturing airbases, factories and depots to grow income and production, while
an aggression-driven enemy commander escalates its response. The campaign
spans many sessions and is won by capturing the enemy capital.

The mode is delivered as:

1. A **mission file** built in the vanilla mission editor (map layout, factions, special bases).
2. A **BepInEx 5 + Harmony plugin**, installed on the **host only**, which
   replaces the economy, AI deployment, escalation and win/lose rules while that
   mission is running, and persists campaign state between sessions.

### Decisions made

| Topic | Decision |
|---|---|
| Players | Co-op on a player-hosted game; only the host needs the mod |
| Campaign length | Multi-session, saved between sessions |
| Map | Heartland |
| Start | Rebels hold a single small airstrip; occupiers hold everything else |
| Enemy behaviour | Antistasi-style aggression meter driving an enemy commander |
| Rebel growth | Players plus captured production (captured factories/depots produce friendly AI) |
| Lose | Rebels hold zero airbases |
| Win | Capture the enemy capital, which unlocks after taking enough ground |
| Nukes | Vanilla escalation disabled; warheads are moved by interceptable ground convoys |
| Architecture | Host-side plugin + custom mission file |
| Future feature | Difficulty / campaign-speed presets (out of scope for v1, enabled by config) |

## 2. Relevant vanilla behaviour (from decompiled `Assembly-CSharp.dll`)

- Networking uses **Mirage**. Economy, AI deployment and capture logic are
  server-authoritative (`IsServer` checks); in co-op the host is the server.
  Faction funds, airbase ownership and supply counts are `SyncVar`/`SyncList`
  values, so changes made through the game's own methods replicate to unmodded
  clients.
- `FactionHQ` owns the faction economy:
  - `DistributeFunds()` runs every 30 s: pays
    `bonusIncome + (regularIncome + excessShare) * players * 0.5` to players as allocation.
  - `AddFunds`, `SetFunds`, `AddBonusFunds`, `AddScore`/`SetScore`.
  - `SetScore` → `CheckEscalation()` raises `MissionManager.currentEscalation`
    against `tacticalThreshold` / `strategicThreshold` (vanilla nuclear clearance).
  - `OnMissionLoad(Mission)` copies `MissionFaction` settings (starting balance,
    income, AI limits, supplies, warheads) into the HQ.
- AI deployment (`FactionHQ.DeployUnits`, every 5 s):
  - `DeployAIAircraft()` launches AI aircraft from nearest airbases while
    `activeAIAircraft.Count < AIAircraftLimit + enemyPlayers*addAIPerEnemyPlayer - friendlyPlayers*reduceAIPerFriendlyPlayer`.
  - `DeployVehicles()` spawns any vehicles in `VehicleSupply` from depots.
  - There is no strategic commander; units pick targets individually
    (e.g. `TryGetNearestGroundEnemy`).
- Production: `Factory` credits output to `attachedUnit.NetworkHQ.AddSupplyUnit(...)`,
  i.e. to the factory's current owner.
- Warheads are physical: stored in `WarheadStorage` buildings attached to
  airbases (`Airbase.GetWarheads/AddWarheads/RemoveWarheads`). AI can use
  `GetWarheadStockpile() - warheadsReserve`.
- Capture: `Capture` component (`ForceCapture`, `SetCapturable`, `ApplyChange`);
  `Airbase` exposes `CurrentHQ`, `onTakeControl`, `onLostControl`;
  `FactionHQ` exposes `onAirbaseAdded/onAirbaseRemoved`.
- Messaging: `MessageManager.RpcAllHQMessage(string)` reaches all clients.
- End of game: `FactionHQ.DeclareEndGame(EndType)`.
- Official chat-command API: `NuclearOption.ModdingApi.ChatCommands.RegisterServerCommand(...)`.
- Mission identity: `MissionManager.CurrentMission.Name`, event `MissionManager.onMissionLoad`.

Exact patch targets are confirmed during implementation; names above are from
the current game build and may change with game updates.

## 3. Architecture

### 3.1 Mission file — `Nuclearstasi – Heartland`

Built in the vanilla editor and shipped alongside the plugin.

- Two factions: **Rebels** (players) and **Occupiers**.
- Rebels own one small airstrip. Occupiers own every other airbase, factory and depot.
- Special bases are identified by name prefix:
  - `[CAPITAL]` — the enemy capital (win objective).
  - `[NUKE]` — the enemy nuclear depot (holds all warheads at start; needs warhead storage).
- Rebel players only; occupier faction has `preventJoin` set.

### 3.2 Plugin activation

On `MissionManager.onMissionLoad`, if running as server and the mission name
starts with `Nuclearstasi`, the plugin activates its modules. Otherwise all
patches are pass-through and vanilla behaviour is unchanged.

### 3.3 Modules

Each module is a small class with one job. Gameplay logic lives in a pure C#
core (no Unity/game references) so it can be unit tested; a thin adapter layer
connects it to the game via Harmony patches and game APIs.

| Module | Responsibility |
|---|---|
| **CampaignState** | Single source of persisted state; JSON save/load |
| **Economy** | Territory-based rebel income; occupier reinforcement rate |
| **Aggression** | 0–100 meter: gains, decay, floor, tiers |
| **EnemyCommander** | Translates tier into patrols, quick reactions, strikes, counter-attacks |
| **Production** | Captured factory output, rebel AI cap, garrison behaviour |
| **NukeConvoys** | Warhead convoy dispatch, tracking, delivery, announcements |
| **Victory** | Capital lock/unlock, win and lose checks |
| **Announcer** | Formats and sends HQ messages to all players |
| **Commands** | `/ns` chat commands |
| **Config** | BepInEx config bindings for every tunable number |

Data flow: game events (mission load, airbase captured, unit killed, building
destroyed, convoy truck destroyed, timers) → Harmony patches/event handlers →
modules → game changed only through its own methods (`AddFunds`, `AddWarheads`,
spawn calls, AI limits), preserving network sync.

## 4. Gameplay rules (v1 defaults; all configurable)

### 4.1 Economy

**Rebel income.** Paid on vanilla's 30 s `DistributeFunds` tick, shared among
rebel players as vanilla does. The plugin sets the rebel HQ's `regularIncome`
before each payout so vanilla payout and sync code is reused.

| Source | Income per tick |
|---|---|
| Base trickle (always) | 2 |
| Small airstrip | 3 |
| Large airbase | 6 |
| Factory | 4 |
| Vehicle depot | 2 |

Kill, capture, repair and resupply rewards remain vanilla.

**Occupier strength.** The occupier AI spends supply stockpiles, not funds.
- Large starting stockpile (set in the mission file).
- **Off-map reinforcements:** periodic supply top-up scaled by the occupier's
  share of all sites held (near-endless at ~95%, a trickle at ~40%).
- Their factories add production; capturing or destroying them cuts it.

### 4.2 Captured production

- Captured factories produce for rebels (vanilla behaviour; verified/ensured by plugin).
- Cap on simultaneous rebel AI units on the map.
- Rebel ground units garrison near their spawning base instead of driving to
  the nearest enemy; rebel AI aircraft fly from rebel airbases as vanilla.

### 4.3 Aggression

Range 0–100.

| Rebel action | Aggression |
|---|---|
| Kill enemy aircraft | +2 |
| Kill enemy ground vehicle / ship | +1 |
| Destroy building | +3 |
| Capture factory / depot | +10 |
| Capture airbase | +15 |
| Destroy a nuke convoy | +5 |
| Any of the above within 15 km of the capital | ×2 |

- **Decay:** −1 per 2 minutes.
- **Floor:** `50 × rebel share of all sites`.
- Tier changes are announced (e.g. "Occupation forces are now on HIGH ALERT").
- v1 counts kills reported to the rebel HQ by players
  (`FactionHQ.ReportKillAction`); kills by rebel AI do not raise aggression.
- "Capture factory / depot" applies per factory or depot inside a captured
  airbase, on top of the airbase's own +15.

### 4.4 Enemy commander

Re-plans every 60 s based on tier.

| Tier | Range | Behaviour |
|---|---|---|
| Quiet | 0–24 | 1–2 AI aircraft patrolling near own bases; ground units garrison only |
| Alert | 25–49 | 3–4 patrolling aircraft; **quick reaction**: 2 aircraft from nearest enemy airbase to a site the rebels just hit (cooldown 5 min) |
| Hostile | 50–74 | Quick reactions of 4 aircraft; **strike** on a rebel base every ~15 min |
| Total War | 75–100 | **Counter-attacks**: combined air + ground push on the rebels' newest/weakest base, announced ~3 min ahead; nuke convoys enabled |

Mechanism: the commander overrides the occupier's effective AI aircraft limit
per tier (replacing the vanilla flat formula) and directs aircraft/convoys to
destinations; vanilla AI handles combat en route. Without orders, occupier
ground units garrison rather than seeking the nearest enemy.

**Known risk:** directing AI to a specific destination depends on AI internals
not yet fully investigated. Fallback: spawn the response force near the
target so vanilla AI engages it naturally. Gameplay outcome is the same.

### 4.5 Nuke convoys

- Vanilla escalation is disabled for this mode (`CheckEscalation` has no effect
  and AI nuclear clearance is controlled by the plugin).
- All occupier warheads start at the `[NUKE]` depot (default 6); other bases have 0.
- **Dispatch conditions:** tier is Total War, at least 20 min since the last
  convoy, and no convoy currently on the road.
- **Route:** from the nuke depot to the occupier airbase with warhead storage
  closest to rebel territory.
- **Composition:** 2 warhead trucks carrying 1 warhead each (2 total), escorted
  by 2 AA vehicles and 2 armoured vehicles.
- **Announcements:** on departure ("INTEL: Nuclear convoy departed [depot] for
  [airbase] – ETA ~X min"), at halfway, and at 2 minutes out.
- **Outcome:**
  - Each truck destroyed removes its warhead from the delivery. If both trucks
    are destroyed: "Nuclear convoy destroyed", +5 aggression.
  - Convoy arrives: surviving warheads are added to the destination airbase
    (`AddWarheads`); AI aircraft there may carry nukes; players are warned.
- **Captured warheads:** capturing a base holding warheads gives them to the
  rebels (vanilla). Config option `RebelsCanUseCapturedWarheads` (default `true`).
- Until nuke convoys are implemented (Plan 3), nuclear weapons are fully
  disabled in this mode.

### 4.6 Victory and defeat

- **Capital lock:** while locked, the `[CAPITAL]` base cannot be captured
  (plugin blocks capture progress).
- **Unlock:** when rebels hold ≥ 50% of the map's airbases. Announced:
  "The capital's defences are exposed". Afterwards aggression is pinned at
  Total War and the commander prioritises defending the capital.
- **Win:** rebels capture the capital → announcement + vanilla end-game flow
  (`DeclareEndGame`).
- **Lose:** rebels hold zero airbases → same flow; save marked as lost.

## 5. Persistence

- File: `BepInEx/config/Nuclearstasi/campaign.json` on the host.
- Saved: ownership of every airbase (factories and depots inside an airbase
  change hands with it); aggression; warheads per base; the **rebel
  treasury** (rebel faction funds plus every connected rebel player's funds —
  vanilla returns a leaving player's funds to the faction pool, so funds are
  kept as one pool rather than per Steam ID); unit supply stockpiles; capital
  unlocked flag; elapsed campaign time; campaign status (active / won / lost);
  save format version. On load the treasury goes back into the rebel pool and
  reaches players through vanilla's join allowance and excess-funds payout.
- Not saved: units in the field. On reload, garrisons respawn from stockpiles;
  in-progress convoys and counter-attacks are cancelled.
- Saved every 5 minutes and when the mission is quit.
- Load: no save → new campaign; save present → applied immediately after
  mission load. `/ns new` wipes the save and starts a new campaign.
- Errors: an unreadable/corrupt save is renamed to `campaign.bad.json`, an HQ
  message is shown, and a new campaign starts. Save failures are logged and
  never crash the game.

## 6. Configuration and commands

- All numbers in section 4 are BepInEx config entries in `Nuclearstasi.cfg`.
  Future difficulty presets will be sets of these values.
- Chat commands (via the game's `ChatCommands` API):
  - `/ns status` — aggression, tier, territory share, warhead locations (all players).
  - `/ns new` — start a new campaign (host only).
  - `/ns aggro <n>`, `/ns convoy`, `/ns unlock` — debug commands (host only).

## 7. Build and repository

- Target: .NET Framework 4.x (game assembly targets `net40`), C# language
  version supported by the game's Mono runtime.
- Projects:
  - `Nuclearstasi.Core` — pure gameplay logic, no game references.
  - `Nuclearstasi.Core.Tests` — unit tests for the core.
  - `Nuclearstasi.Plugin` — BepInEx plugin, Harmony patches, game adapters.
- Game and BepInEx DLLs (`Assembly-CSharp.dll`, `Mirage.dll`, `UniTask.dll`,
  `UnityEngine*.dll`, `BepInEx.dll`, `0Harmony.dll`) are referenced from a
  local `lib/` folder that is git-ignored and never committed.

## 8. Testing

- **Unit tests** (automated): aggression gains/decay/floor/tiers, income
  calculation, reinforcement scaling, commander tier decisions, convoy
  dispatch rules and delivery maths, capital unlock, win/lose checks,
  save/load round-trip and corrupt-save handling.
- **In-game** (by the user): each module logs decisions to
  `BepInEx/LogOutput.log`; debug commands allow testing late-game features
  quickly. Vanilla missions must be verified to behave unchanged with the
  plugin installed.

## 9. Out of scope for v1

- Difficulty / campaign-speed presets (config groundwork is in v1).
- Custom in-game UI or buy menus (would require the mod on all clients).
- Maps other than Heartland.
- Dedicated-server support.
- Persisting units in the field.
