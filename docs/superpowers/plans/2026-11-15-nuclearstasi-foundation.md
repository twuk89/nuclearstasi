# Nuclearstasi Foundation Implementation Plan (Plan 1 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A host-only BepInEx plugin that, on a mission named `Nuclearstasi…`, replaces vanilla income with territory-based income, runs the aggression meter with tier announcements, sends occupier reinforcements scaled by territory, disables vanilla nuclear escalation, saves/restores the campaign between sessions, and provides the `/ns` chat command.

**Architecture:** Pure gameplay logic lives in `Nuclearstasi.Core` (no Unity/game references, unit tested on .NET 8). `Nuclearstasi.Plugin` (net472) is a thin adapter: Harmony patches forward game events to a `CampaignController`, which uses Core classes and changes the game only through its own methods so Mirage syncs everything to unmodded clients.

**Tech Stack:** C# 9, .NET SDK 8, net472 + net8.0, BepInEx 5.4.23, HarmonyX 2.9, Newtonsoft.Json 13 (shipped with the game), xUnit.

**Roadmap:** Plan 2 = Enemy Commander + captured production/garrisons. Plan 3 = nuke convoys, capital lock & victory, shipped mission file. This plan delivers the first in-game-testable milestone.

## Global Constraints

- Plugin is **host-only**: every behaviour is gated on `GameQueries.IsServer()`; clients without the mod must still work.
- Activates only when `MissionManager.CurrentMission.Name` starts with `Nuclearstasi` (case-insensitive). Otherwise vanilla behaviour is unchanged.
- Change game state only through game methods (`AddFunds`, `SetFunds`, `AddSupplyUnit`, `AddWarheads`, `RemoveWarheads`, `Capture.ForceCapture`, `RpcAllHQMessage`) so values replicate.
- Game/BepInEx DLLs live in git-ignored `lib/` and are **never committed**. Required: `Assembly-CSharp.dll`, `Mirage.dll`, `UniTask.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `BepInEx.dll`, `0Harmony.dll`.
- Do not ship `Newtonsoft.Json.dll`; the game provides v13.0.0.0.
- C# 9 only (no `init`, no records) because Core also targets net472.
- Every tunable number is a BepInEx config entry (file `BepInEx/config/com.nuclearstasi.mod.cfg`).
- Save file: `BepInEx/config/Nuclearstasi/campaign.json`; corrupt saves are renamed `campaign.bad.json` and never crash the game.
- No model identifiers or personal details in code, comments or commits.

## Design adjustments discovered while planning (fold into spec in Task 10)

1. Factories and depots are buildings **inside airbases** and change hands with them. A "site" is counted per airbase plus each factory/depot inside it.
2. Vanilla returns a leaving player's personal funds to the faction pool (`FactionHQ.RemovePlayer`). So instead of per-Steam-ID funds, the save stores one **rebel treasury** (faction funds + every connected rebel player's funds). On load it is put back in the faction pool and reaches players through vanilla's join allowance and excess-funds payout.
3. Aggression from kills counts kills reported to the rebel HQ by players (`FactionHQ.ReportKillAction`). Kills by rebel AI do not raise aggression in v1.
4. Nuclear escalation is fully disabled in this plan. Plan 3 re-enables nukes through convoys.

## File structure

```
Directory.Build.props                      shared C# settings + GameLibDir
Nuclearstasi.sln
scripts/package.sh                         builds Release and assembles dist/
src/Nuclearstasi.Core/
  Nuclearstasi.Core.csproj                 net472;net8.0
  Settings.cs                              all tunable numbers (POCOs)
  Territory.cs                             SiteCounts, Territory
  Economy.cs                               rebel income maths
  Aggression.cs                            AggressionEvent, Tier, TierChange, AggressionMeter
  Reinforcements.cs                        ReinforcementPlanner
  CampaignState.cs                         persisted state POCO
  CampaignStore.cs                         JSON save/load/corrupt handling
  Messages.cs                              player-facing text + /ns status line
src/Nuclearstasi.Plugin/
  Nuclearstasi.Plugin.csproj               net472, AssemblyName Nuclearstasi
  Plugin.cs                                BepInEx entry point
  PluginConfig.cs                          binds Settings to BepInEx config
  Game/GameQueries.cs                      read-only game lookups
  Game/Announcer.cs                        HQ messages to all players
  Game/StateSync.cs                        game <-> CampaignState
  Campaign/CampaignController.cs           orchestrates everything
  Patches/MissionPatches.cs                start/unload/destroy hooks
  Patches/FactionHQPatches.cs              funds, kills, escalation hooks
  Commands/NsCommand.cs                    /ns chat command
tests/Nuclearstasi.Core.Tests/
  Nuclearstasi.Core.Tests.csproj           net8.0 xUnit
  EconomyTests.cs, AggressionTests.cs, ReinforcementTests.cs,
  CampaignStoreTests.cs, MessagesTests.cs
```

---

### Task 1: Solution scaffold, territory and economy

**Files:**
- Create: `Directory.Build.props`, `Nuclearstasi.sln` (via CLI)
- Create: `src/Nuclearstasi.Core/Nuclearstasi.Core.csproj`, `src/Nuclearstasi.Core/Settings.cs`, `src/Nuclearstasi.Core/Territory.cs`, `src/Nuclearstasi.Core/Economy.cs`
- Create: `tests/Nuclearstasi.Core.Tests/Nuclearstasi.Core.Tests.csproj`
- Test: `tests/Nuclearstasi.Core.Tests/EconomyTests.cs`

**Interfaces:**
- Produces:
  - `Settings { EconomySettings Economy; AggressionSettings Aggression; ReinforcementSettings Reinforcements; CampaignSettings Campaign; }` (all public mutable fields with defaults below)
  - `struct SiteCounts { int SmallAirbases, LargeAirbases, Factories, Depots; int Total; SiteCounts Plus(SiteCounts) }`
  - `class Territory { SiteCounts Rebel, Occupier, Neutral; int TotalSites; double RebelShare; double OccupierShare }`
  - `static float Economy.RebelIncome(SiteCounts rebel, EconomySettings s)`
  - `static float Economy.RegularIncomeForPayout(float income, int playerCount)`

- [ ] **Step 1: Create shared build props**

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <GameLibDir>$(MSBuildThisFileDirectory)lib/</GameLibDir>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create Core project**

`src/Nuclearstasi.Core/Nuclearstasi.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net472;net8.0</TargetFrameworks>
    <RootNamespace>Nuclearstasi.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

`src/Nuclearstasi.Core/Settings.cs`:
```csharp
namespace Nuclearstasi.Core
{
    public sealed class Settings
    {
        public EconomySettings Economy = new EconomySettings();
        public AggressionSettings Aggression = new AggressionSettings();
        public ReinforcementSettings Reinforcements = new ReinforcementSettings();
        public CampaignSettings Campaign = new CampaignSettings();
    }

    public sealed class EconomySettings
    {
        public float BaseTrickle = 2f;
        public float SmallAirbase = 3f;
        public float LargeAirbase = 6f;
        public float Factory = 4f;
        public float Depot = 2f;
        public float LargeAirbaseRunwayMetres = 1800f;
    }

    public sealed class AggressionSettings
    {
        public double AircraftKilled = 2;
        public double GroundUnitKilled = 1;
        public double BuildingDestroyed = 3;
        public double FactoryOrDepotCaptured = 10;
        public double AirbaseCaptured = 15;
        public double ConvoyDestroyed = 5;
        public double CapitalRadiusMetres = 15000;
        public double CapitalMultiplier = 2;
        public double DecayAmount = 1;
        public double DecayIntervalSeconds = 120;
        public double FloorPerRebelShare = 50;
        public double AlertAt = 25;
        public double HostileAt = 50;
        public double TotalWarAt = 75;
    }

    public sealed class ReinforcementSettings
    {
        public double IntervalSeconds = 600;
        public double WaveFraction = 0.25;
        public double ShareExponent = 3;
    }

    public sealed class CampaignSettings
    {
        public string MissionPrefix = "Nuclearstasi";
        public string CapitalPrefix = "[CAPITAL]";
        public string NukeDepotPrefix = "[NUKE]";
        public double AutosaveIntervalSeconds = 300;
        public double ApplyDelaySeconds = 3;
    }
}
```

`src/Nuclearstasi.Core/Territory.cs`:
```csharp
namespace Nuclearstasi.Core
{
    public struct SiteCounts
    {
        public int SmallAirbases;
        public int LargeAirbases;
        public int Factories;
        public int Depots;

        public int Total => SmallAirbases + LargeAirbases + Factories + Depots;

        public SiteCounts Plus(SiteCounts other)
        {
            return new SiteCounts
            {
                SmallAirbases = SmallAirbases + other.SmallAirbases,
                LargeAirbases = LargeAirbases + other.LargeAirbases,
                Factories = Factories + other.Factories,
                Depots = Depots + other.Depots,
            };
        }
    }

    public sealed class Territory
    {
        public SiteCounts Rebel;
        public SiteCounts Occupier;
        public SiteCounts Neutral;

        public int TotalSites => Rebel.Total + Occupier.Total + Neutral.Total;

        public double RebelShare => TotalSites == 0 ? 0 : (double)Rebel.Total / TotalSites;

        public double OccupierShare => TotalSites == 0 ? 0 : (double)Occupier.Total / TotalSites;
    }
}
```

- [ ] **Step 3: Create test project and solution**

`tests/Nuclearstasi.Core.Tests/Nuclearstasi.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.6.0" />
    <PackageReference Include="xunit" Version="2.4.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.4.5" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Nuclearstasi.Core/Nuclearstasi.Core.csproj" />
  </ItemGroup>
</Project>
```

Run:
```bash
dotnet new sln -n Nuclearstasi
dotnet sln add src/Nuclearstasi.Core/Nuclearstasi.Core.csproj tests/Nuclearstasi.Core.Tests/Nuclearstasi.Core.Tests.csproj
```

- [ ] **Step 4: Write the failing tests**

`tests/Nuclearstasi.Core.Tests/EconomyTests.cs`:
```csharp
using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public class EconomyTests
    {
        private readonly EconomySettings settings = new EconomySettings();

        [Fact]
        public void SingleSmallAirstripEarnsTricklePlusAirstrip()
        {
            var rebel = new SiteCounts { SmallAirbases = 1 };
            Assert.Equal(5f, Economy.RebelIncome(rebel, settings));
        }

        [Fact]
        public void IncomeSumsEverySiteType()
        {
            var rebel = new SiteCounts { LargeAirbases = 1, Factories = 2, Depots = 1 };
            Assert.Equal(2f + 6f + 8f + 2f, Economy.RebelIncome(rebel, settings));
        }

        [Fact]
        public void NoTerritoryStillEarnsTrickle()
        {
            Assert.Equal(2f, Economy.RebelIncome(new SiteCounts(), settings));
        }

        [Theory]
        [InlineData(9f, 3)]
        [InlineData(5f, 1)]
        [InlineData(12f, 4)]
        public void RegularIncomeMakesVanillaPayoutEqualIncome(float income, int players)
        {
            float regular = Economy.RegularIncomeForPayout(income, players);
            // vanilla DistributeFunds pays regularIncome * players * 0.5
            Assert.Equal(income, regular * players * 0.5f, 3);
        }

        [Fact]
        public void RegularIncomeIsZeroWithNoPlayers()
        {
            Assert.Equal(0f, Economy.RegularIncomeForPayout(10f, 0));
        }

        [Fact]
        public void TerritorySharesUseAllSites()
        {
            var t = new Territory
            {
                Rebel = new SiteCounts { SmallAirbases = 1 },
                Occupier = new SiteCounts { LargeAirbases = 2, Factories = 1 },
            };
            Assert.Equal(4, t.TotalSites);
            Assert.Equal(0.25, t.RebelShare, 6);
            Assert.Equal(0.75, t.OccupierShare, 6);
        }

        [Fact]
        public void EmptyTerritoryHasZeroShares()
        {
            var t = new Territory();
            Assert.Equal(0, t.RebelShare);
            Assert.Equal(0, t.OccupierShare);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: build FAILS with `CS0103: The name 'Economy' does not exist`.

- [ ] **Step 6: Implement Economy**

`src/Nuclearstasi.Core/Economy.cs`:
```csharp
namespace Nuclearstasi.Core
{
    public static class Economy
    {
        public static float RebelIncome(SiteCounts rebel, EconomySettings s)
        {
            return s.BaseTrickle
                + rebel.SmallAirbases * s.SmallAirbase
                + rebel.LargeAirbases * s.LargeAirbase
                + rebel.Factories * s.Factory
                + rebel.Depots * s.Depot;
        }

        /// <summary>
        /// Vanilla FactionHQ.DistributeFunds pays (regularIncome + excess) * players * 0.5.
        /// Returns the regularIncome that makes the regular part equal <paramref name="income"/>.
        /// </summary>
        public static float RegularIncomeForPayout(float income, int playerCount)
        {
            if (playerCount <= 0)
            {
                return 0f;
            }
            return income * 2f / playerCount;
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Passed!  - Failed: 0, Passed: 9`

- [ ] **Step 8: Commit**

```bash
git add Directory.Build.props Nuclearstasi.sln src tests
git commit -m "Add core project with territory and economy rules"
```

---

### Task 2: Aggression meter

**Files:**
- Create: `src/Nuclearstasi.Core/Aggression.cs`
- Test: `tests/Nuclearstasi.Core.Tests/AggressionTests.cs`

**Interfaces:**
- Consumes: `AggressionSettings` (Task 1)
- Produces:
  - `enum AggressionEvent { AircraftKilled, GroundUnitKilled, BuildingDestroyed, FactoryOrDepotCaptured, AirbaseCaptured, ConvoyDestroyed }`
  - `enum Tier { Quiet, Alert, Hostile, TotalWar }`
  - `struct TierChange { Tier From; Tier To; bool Changed; bool Rose }`
  - `class AggressionMeter(AggressionSettings s, double initial)` with `double Value`, `Tier Tier`, `Tier TierFor(double)`, `double Floor(double rebelShare)`, `TierChange Add(AggressionEvent e, bool nearCapital)`, `TierChange Tick(double seconds, double rebelShare)`, `TierChange Set(double value)`

- [ ] **Step 1: Write the failing tests**

`tests/Nuclearstasi.Core.Tests/AggressionTests.cs`:
```csharp
using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public class AggressionTests
    {
        private static AggressionMeter Meter(double initial = 0)
        {
            return new AggressionMeter(new AggressionSettings(), initial);
        }

        [Fact]
        public void StartsQuietAtInitialValue()
        {
            var m = Meter(10);
            Assert.Equal(10, m.Value);
            Assert.Equal(Tier.Quiet, m.Tier);
        }

        [Fact]
        public void CapturingAnAirbaseAddsFifteen()
        {
            var m = Meter();
            m.Add(AggressionEvent.AirbaseCaptured, nearCapital: false);
            Assert.Equal(15, m.Value);
        }

        [Fact]
        public void ActionsNearCapitalAreDoubled()
        {
            var m = Meter();
            m.Add(AggressionEvent.AircraftKilled, nearCapital: true);
            Assert.Equal(4, m.Value);
        }

        [Fact]
        public void ValueIsClampedTo100()
        {
            var m = Meter(95);
            m.Add(AggressionEvent.AirbaseCaptured, nearCapital: false);
            Assert.Equal(100, m.Value);
        }

        [Theory]
        [InlineData(0, Tier.Quiet)]
        [InlineData(24.9, Tier.Quiet)]
        [InlineData(25, Tier.Alert)]
        [InlineData(49.9, Tier.Alert)]
        [InlineData(50, Tier.Hostile)]
        [InlineData(75, Tier.TotalWar)]
        [InlineData(100, Tier.TotalWar)]
        public void TiersFollowThresholds(double value, Tier expected)
        {
            Assert.Equal(expected, Meter().TierFor(value));
        }

        [Fact]
        public void AddReportsTierRise()
        {
            var m = Meter(20);
            TierChange change = m.Add(AggressionEvent.AirbaseCaptured, nearCapital: false);
            Assert.True(change.Changed);
            Assert.True(change.Rose);
            Assert.Equal(Tier.Quiet, change.From);
            Assert.Equal(Tier.Alert, change.To);
        }

        [Fact]
        public void AddWithinTierReportsNoChange()
        {
            var m = Meter(0);
            TierChange change = m.Add(AggressionEvent.GroundUnitKilled, nearCapital: false);
            Assert.False(change.Changed);
        }

        [Fact]
        public void DecaysOnePointPerTwoMinutes()
        {
            var m = Meter(10);
            m.Tick(240, rebelShare: 0);
            Assert.Equal(8, m.Value);
        }

        [Fact]
        public void PartialTicksAccumulate()
        {
            var m = Meter(10);
            m.Tick(100, rebelShare: 0);
            Assert.Equal(10, m.Value);
            m.Tick(30, rebelShare: 0);
            Assert.Equal(9, m.Value);
        }

        [Fact]
        public void FloorRaisesValueAndReportsTierChange()
        {
            var m = Meter(0);
            TierChange change = m.Tick(1, rebelShare: 0.5);
            Assert.Equal(25, m.Value);
            Assert.Equal(Tier.Alert, change.To);
            Assert.True(change.Rose);
        }

        [Fact]
        public void DecayStopsAtFloor()
        {
            var m = Meter(30);
            m.Tick(1200, rebelShare: 0.5);
            Assert.Equal(25, m.Value);
        }

        [Fact]
        public void DecayNeverGoesBelowZero()
        {
            var m = Meter(1);
            m.Tick(1200, rebelShare: 0);
            Assert.Equal(0, m.Value);
        }

        [Fact]
        public void SetClampsAndReportsChange()
        {
            var m = Meter(0);
            TierChange change = m.Set(150);
            Assert.Equal(100, m.Value);
            Assert.Equal(Tier.TotalWar, change.To);
            m.Set(-5);
            Assert.Equal(0, m.Value);
        }

        [Fact]
        public void FloorIsFiftyTimesShare()
        {
            Assert.Equal(10, Meter().Floor(0.2), 6);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: build FAILS with `CS0246: The type or namespace name 'AggressionMeter' could not be found`.

- [ ] **Step 3: Implement the meter**

`src/Nuclearstasi.Core/Aggression.cs`:
```csharp
using System;

namespace Nuclearstasi.Core
{
    public enum AggressionEvent
    {
        AircraftKilled,
        GroundUnitKilled,
        BuildingDestroyed,
        FactoryOrDepotCaptured,
        AirbaseCaptured,
        ConvoyDestroyed,
    }

    public enum Tier
    {
        Quiet = 0,
        Alert = 1,
        Hostile = 2,
        TotalWar = 3,
    }

    public struct TierChange
    {
        public Tier From;
        public Tier To;

        public bool Changed => From != To;

        public bool Rose => To > From;
    }

    public sealed class AggressionMeter
    {
        private const double Min = 0;
        private const double Max = 100;

        private readonly AggressionSettings settings;
        private double decayTimer;

        public AggressionMeter(AggressionSettings settings, double initial)
        {
            this.settings = settings;
            Value = Clamp(initial);
        }

        public double Value { get; private set; }

        public Tier Tier => TierFor(Value);

        public Tier TierFor(double value)
        {
            if (value >= settings.TotalWarAt) return Tier.TotalWar;
            if (value >= settings.HostileAt) return Tier.Hostile;
            if (value >= settings.AlertAt) return Tier.Alert;
            return Tier.Quiet;
        }

        public double Floor(double rebelShare)
        {
            return Clamp(settings.FloorPerRebelShare * rebelShare);
        }

        public TierChange Add(AggressionEvent e, bool nearCapital)
        {
            double amount = AmountFor(e);
            if (nearCapital)
            {
                amount *= settings.CapitalMultiplier;
            }
            return Set(Value + amount);
        }

        public TierChange Tick(double seconds, double rebelShare)
        {
            double next = Value;
            decayTimer += seconds;
            while (decayTimer >= settings.DecayIntervalSeconds)
            {
                decayTimer -= settings.DecayIntervalSeconds;
                next -= settings.DecayAmount;
            }
            next = Math.Max(next, Floor(rebelShare));
            return Set(next);
        }

        public TierChange Set(double value)
        {
            Tier from = Tier;
            Value = Clamp(value);
            return new TierChange { From = from, To = Tier };
        }

        private double AmountFor(AggressionEvent e)
        {
            switch (e)
            {
                case AggressionEvent.AircraftKilled: return settings.AircraftKilled;
                case AggressionEvent.GroundUnitKilled: return settings.GroundUnitKilled;
                case AggressionEvent.BuildingDestroyed: return settings.BuildingDestroyed;
                case AggressionEvent.FactoryOrDepotCaptured: return settings.FactoryOrDepotCaptured;
                case AggressionEvent.AirbaseCaptured: return settings.AirbaseCaptured;
                case AggressionEvent.ConvoyDestroyed: return settings.ConvoyDestroyed;
                default: return 0;
            }
        }

        private static double Clamp(double v)
        {
            return Math.Max(Min, Math.Min(Max, v));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Passed!  - Failed: 0, Passed: 29`

- [ ] **Step 5: Commit**

```bash
git add src/Nuclearstasi.Core/Aggression.cs tests/Nuclearstasi.Core.Tests/AggressionTests.cs
git commit -m "Add aggression meter with tiers, decay and territory floor"
```

---

### Task 3: Occupier reinforcements

**Files:**
- Create: `src/Nuclearstasi.Core/Reinforcements.cs`
- Test: `tests/Nuclearstasi.Core.Tests/ReinforcementTests.cs`

**Interfaces:**
- Consumes: `ReinforcementSettings` (Task 1)
- Produces:
  - `class ReinforcementPlanner(IDictionary<string,int> baseline, ReinforcementSettings s)` — baseline is the occupier's starting supply per unit key (jsonKey)
  - `static double ReinforcementPlanner.Multiplier(double occupierShare, double exponent)`
  - `Dictionary<string,int> NextWave(double occupierShare)` — whole units to add this wave; fractions carry over

- [ ] **Step 1: Write the failing tests**

`tests/Nuclearstasi.Core.Tests/ReinforcementTests.cs`:
```csharp
using System.Collections.Generic;
using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public class ReinforcementTests
    {
        private static ReinforcementPlanner Planner(Dictionary<string, int> baseline)
        {
            return new ReinforcementPlanner(baseline, new ReinforcementSettings());
        }

        [Theory]
        [InlineData(1.0, 1.0)]
        [InlineData(0.4, 0.064)]
        [InlineData(0.95, 0.857375)]
        [InlineData(0.0, 0.0)]
        public void MultiplierIsShareCubed(double share, double expected)
        {
            Assert.Equal(expected, ReinforcementPlanner.Multiplier(share, 3), 6);
        }

        [Fact]
        public void MultiplierClampsShare()
        {
            Assert.Equal(1.0, ReinforcementPlanner.Multiplier(1.5, 3), 6);
            Assert.Equal(0.0, ReinforcementPlanner.Multiplier(-1, 3), 6);
        }

        [Fact]
        public void FullShareSendsQuarterOfBaseline()
        {
            var wave = Planner(new Dictionary<string, int> { ["tank"] = 8 }).NextWave(1.0);
            Assert.Equal(2, wave["tank"]);
        }

        [Fact]
        public void FractionsCarryOverBetweenWaves()
        {
            var planner = Planner(new Dictionary<string, int> { ["tank"] = 2 });
            Assert.False(planner.NextWave(1.0).ContainsKey("tank"));
            Assert.Equal(1, planner.NextWave(1.0)["tank"]);
        }

        [Fact]
        public void NoShareSendsNothing()
        {
            var wave = Planner(new Dictionary<string, int> { ["tank"] = 100 }).NextWave(0);
            Assert.Empty(wave);
        }

        [Fact]
        public void ZeroBaselineEntriesAreIgnored()
        {
            var wave = Planner(new Dictionary<string, int> { ["tank"] = 0, ["jet"] = 4 }).NextWave(1.0);
            Assert.False(wave.ContainsKey("tank"));
            Assert.Equal(1, wave["jet"]);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: build FAILS with `CS0246: ... 'ReinforcementPlanner' could not be found`.

- [ ] **Step 3: Implement the planner**

`src/Nuclearstasi.Core/Reinforcements.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace Nuclearstasi.Core
{
    public sealed class ReinforcementPlanner
    {
        private readonly Dictionary<string, int> baseline;
        private readonly Dictionary<string, double> carry = new Dictionary<string, double>();
        private readonly ReinforcementSettings settings;

        public ReinforcementPlanner(IDictionary<string, int> baseline, ReinforcementSettings settings)
        {
            this.baseline = new Dictionary<string, int>(baseline);
            this.settings = settings;
        }

        public static double Multiplier(double occupierShare, double exponent)
        {
            double share = Math.Max(0, Math.Min(1, occupierShare));
            return Math.Pow(share, exponent);
        }

        public Dictionary<string, int> NextWave(double occupierShare)
        {
            var wave = new Dictionary<string, int>();
            double multiplier = Multiplier(occupierShare, settings.ShareExponent);
            foreach (KeyValuePair<string, int> entry in baseline)
            {
                if (entry.Value <= 0)
                {
                    continue;
                }
                carry.TryGetValue(entry.Key, out double owed);
                owed += entry.Value * settings.WaveFraction * multiplier;
                int whole = (int)Math.Floor(owed + 1e-9);
                carry[entry.Key] = owed - whole;
                if (whole > 0)
                {
                    wave[entry.Key] = whole;
                }
            }
            return wave;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Passed!  - Failed: 0, Passed: 38`

- [ ] **Step 5: Commit**

```bash
git add src/Nuclearstasi.Core/Reinforcements.cs tests/Nuclearstasi.Core.Tests/ReinforcementTests.cs
git commit -m "Add occupier reinforcement planner scaled by territory share"
```

---

### Task 4: Campaign state and save store

**Files:**
- Create: `src/Nuclearstasi.Core/CampaignState.cs`, `src/Nuclearstasi.Core/CampaignStore.cs`
- Test: `tests/Nuclearstasi.Core.Tests/CampaignStoreTests.cs`

**Interfaces:**
- Produces:
  - `enum CampaignStatus { Active, Won, Lost }`
  - `class CampaignState { const int CurrentVersion = 1; int Version; string MissionName; CampaignStatus Status; double ElapsedSeconds; double Aggression; bool CapitalUnlocked; float RebelTreasury; Dictionary<string,string> AirbaseOwners; Dictionary<string,int> Warheads; Dictionary<string, Dictionary<string,int>> Supplies; }` — `AirbaseOwners` maps airbase key → faction name (`""` = neutral); `Supplies` maps faction name → unit jsonKey → count
  - `enum LoadOutcome { NotFound, Loaded, Corrupt }`
  - `class LoadResult { LoadOutcome Outcome; CampaignState State; string Error; }`
  - `class CampaignStore(string directory)` with `string SavePath`, `string BadPath`, `void Save(CampaignState)`, `LoadResult Load()`, `void Delete()`

- [ ] **Step 1: Write the failing tests**

`tests/Nuclearstasi.Core.Tests/CampaignStoreTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public sealed class CampaignStoreTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "nsTest_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        private static CampaignState Sample()
        {
            return new CampaignState
            {
                MissionName = "Nuclearstasi - Heartland",
                Status = CampaignStatus.Active,
                ElapsedSeconds = 3600.5,
                Aggression = 42,
                CapitalUnlocked = true,
                RebelTreasury = 123.25f,
                AirbaseOwners = new Dictionary<string, string> { ["base_a"] = "Rebels", ["base_b"] = "" },
                Warheads = new Dictionary<string, int> { ["base_c"] = 6 },
                Supplies = new Dictionary<string, Dictionary<string, int>>
                {
                    ["Occupiers"] = new Dictionary<string, int> { ["tank"] = 12 },
                },
            };
        }

        [Fact]
        public void LoadWithoutFileIsNotFound()
        {
            Assert.Equal(LoadOutcome.NotFound, new CampaignStore(dir).Load().Outcome);
        }

        [Fact]
        public void SaveThenLoadRoundTrips()
        {
            var store = new CampaignStore(dir);
            store.Save(Sample());
            LoadResult result = store.Load();
            Assert.Equal(LoadOutcome.Loaded, result.Outcome);
            CampaignState s = result.State;
            Assert.Equal("Nuclearstasi - Heartland", s.MissionName);
            Assert.Equal(3600.5, s.ElapsedSeconds);
            Assert.Equal(42, s.Aggression);
            Assert.True(s.CapitalUnlocked);
            Assert.Equal(123.25f, s.RebelTreasury);
            Assert.Equal("Rebels", s.AirbaseOwners["base_a"]);
            Assert.Equal("", s.AirbaseOwners["base_b"]);
            Assert.Equal(6, s.Warheads["base_c"]);
            Assert.Equal(12, s.Supplies["Occupiers"]["tank"]);
        }

        [Fact]
        public void StatusIsStoredAsText()
        {
            var store = new CampaignStore(dir);
            store.Save(Sample());
            Assert.Contains("\"Status\": \"Active\"", File.ReadAllText(store.SavePath));
        }

        [Fact]
        public void CorruptFileIsMovedAsideAndReported()
        {
            var store = new CampaignStore(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(store.SavePath, "{ not json");
            LoadResult result = store.Load();
            Assert.Equal(LoadOutcome.Corrupt, result.Outcome);
            Assert.NotNull(result.Error);
            Assert.False(File.Exists(store.SavePath));
            Assert.True(File.Exists(store.BadPath));
        }

        [Fact]
        public void NewerSaveVersionIsTreatedAsCorrupt()
        {
            var store = new CampaignStore(dir);
            CampaignState s = Sample();
            s.Version = CampaignState.CurrentVersion + 1;
            store.Save(s);
            Assert.Equal(LoadOutcome.Corrupt, store.Load().Outcome);
        }

        [Fact]
        public void MissingCollectionsAreFilledIn()
        {
            var store = new CampaignStore(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(store.SavePath, "{ \"Version\": 1, \"Aggression\": 5, \"AirbaseOwners\": null }");
            LoadResult result = store.Load();
            Assert.Equal(LoadOutcome.Loaded, result.Outcome);
            Assert.NotNull(result.State.AirbaseOwners);
            Assert.NotNull(result.State.Warheads);
            Assert.NotNull(result.State.Supplies);
        }

        [Fact]
        public void DeleteRemovesSave()
        {
            var store = new CampaignStore(dir);
            store.Save(Sample());
            store.Delete();
            Assert.Equal(LoadOutcome.NotFound, store.Load().Outcome);
        }

        [Fact]
        public void SaveOverwritesPreviousSave()
        {
            var store = new CampaignStore(dir);
            store.Save(Sample());
            CampaignState s = Sample();
            s.Aggression = 77;
            store.Save(s);
            Assert.Equal(77, store.Load().State.Aggression);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: build FAILS with `CS0246: ... 'CampaignStore' could not be found`.

- [ ] **Step 3: Implement state and store**

`src/Nuclearstasi.Core/CampaignState.cs`:
```csharp
using System.Collections.Generic;

namespace Nuclearstasi.Core
{
    public enum CampaignStatus
    {
        Active,
        Won,
        Lost,
    }

    public sealed class CampaignState
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string MissionName = "";
        public CampaignStatus Status = CampaignStatus.Active;
        public double ElapsedSeconds;
        public double Aggression;
        public bool CapitalUnlocked;
        public float RebelTreasury;

        /// <summary>Airbase key → owning faction name ("" = neutral).</summary>
        public Dictionary<string, string> AirbaseOwners = new Dictionary<string, string>();

        /// <summary>Airbase key → stored warheads.</summary>
        public Dictionary<string, int> Warheads = new Dictionary<string, int>();

        /// <summary>Faction name → unit jsonKey → stockpiled count.</summary>
        public Dictionary<string, Dictionary<string, int>> Supplies = new Dictionary<string, Dictionary<string, int>>();

        public void EnsureCollections()
        {
            if (MissionName == null) MissionName = "";
            if (AirbaseOwners == null) AirbaseOwners = new Dictionary<string, string>();
            if (Warheads == null) Warheads = new Dictionary<string, int>();
            if (Supplies == null) Supplies = new Dictionary<string, Dictionary<string, int>>();
        }
    }
}
```

`src/Nuclearstasi.Core/CampaignStore.cs`:
```csharp
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Nuclearstasi.Core
{
    public enum LoadOutcome
    {
        NotFound,
        Loaded,
        Corrupt,
    }

    public sealed class LoadResult
    {
        public LoadOutcome Outcome;
        public CampaignState State;
        public string Error;
    }

    public sealed class CampaignStore
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
        };

        private readonly string directory;

        public CampaignStore(string directory)
        {
            this.directory = directory;
        }

        public string SavePath => Path.Combine(directory, "campaign.json");

        public string BadPath => Path.Combine(directory, "campaign.bad.json");

        private string TempPath => Path.Combine(directory, "campaign.json.tmp");

        public void Save(CampaignState state)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(TempPath, JsonConvert.SerializeObject(state, JsonSettings));
            File.Copy(TempPath, SavePath, overwrite: true);
            File.Delete(TempPath);
        }

        public LoadResult Load()
        {
            if (!File.Exists(SavePath))
            {
                return new LoadResult { Outcome = LoadOutcome.NotFound };
            }
            try
            {
                CampaignState state = JsonConvert.DeserializeObject<CampaignState>(File.ReadAllText(SavePath), JsonSettings);
                if (state == null)
                {
                    return Corrupt("save file is empty");
                }
                if (state.Version > CampaignState.CurrentVersion)
                {
                    return Corrupt($"save version {state.Version} is newer than supported version {CampaignState.CurrentVersion}");
                }
                state.EnsureCollections();
                return new LoadResult { Outcome = LoadOutcome.Loaded, State = state };
            }
            catch (Exception e)
            {
                return Corrupt(e.Message);
            }
        }

        public void Delete()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }

        private LoadResult Corrupt(string error)
        {
            try
            {
                File.Copy(SavePath, BadPath, overwrite: true);
                File.Delete(SavePath);
            }
            catch (Exception e)
            {
                error += "; could not move bad save: " + e.Message;
            }
            return new LoadResult { Outcome = LoadOutcome.Corrupt, Error = error };
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Passed!  - Failed: 0, Passed: 46`

- [ ] **Step 5: Commit**

```bash
git add src/Nuclearstasi.Core/CampaignState.cs src/Nuclearstasi.Core/CampaignStore.cs tests/Nuclearstasi.Core.Tests/CampaignStoreTests.cs
git commit -m "Add campaign state with JSON save store and corrupt-save handling"
```

---

### Task 5: Player-facing messages and status line

**Files:**
- Create: `src/Nuclearstasi.Core/Messages.cs`
- Test: `tests/Nuclearstasi.Core.Tests/MessagesTests.cs`

**Interfaces:**
- Consumes: `Tier`, `TierChange` (Task 2), `Territory` (Task 1)
- Produces:
  - `static string Messages.TierName(Tier)` → `"QUIET" | "ALERT" | "HOSTILE" | "TOTAL WAR"`
  - `static string Messages.TierChanged(TierChange)` → text or `null` when unchanged
  - `static string Messages.Status(double aggression, Tier tier, Territory t, float rebelFunds, double elapsedSeconds)`
  - `static string Messages.CampaignStarted()`, `Messages.CampaignResumed(Tier)`, `Messages.CorruptSave()`, `Messages.AirbaseCaptured(string name)`

- [ ] **Step 1: Write the failing tests**

`tests/Nuclearstasi.Core.Tests/MessagesTests.cs`:
```csharp
using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public class MessagesTests
    {
        [Theory]
        [InlineData(Tier.Quiet, "QUIET")]
        [InlineData(Tier.Alert, "ALERT")]
        [InlineData(Tier.Hostile, "HOSTILE")]
        [InlineData(Tier.TotalWar, "TOTAL WAR")]
        public void TierNames(Tier tier, string expected)
        {
            Assert.Equal(expected, Messages.TierName(tier));
        }

        [Theory]
        [InlineData(Tier.Quiet, Tier.Alert, "Occupation forces are now on ALERT")]
        [InlineData(Tier.Alert, Tier.Hostile, "Occupation forces are now HOSTILE")]
        [InlineData(Tier.Hostile, Tier.TotalWar, "The occupation has declared TOTAL WAR")]
        [InlineData(Tier.Quiet, Tier.TotalWar, "The occupation has declared TOTAL WAR")]
        [InlineData(Tier.Hostile, Tier.Alert, "Occupation forces have stood down to ALERT")]
        [InlineData(Tier.Alert, Tier.Quiet, "Occupation forces have stood down to QUIET")]
        public void TierChangeAnnouncements(Tier from, Tier to, string expected)
        {
            Assert.Equal(expected, Messages.TierChanged(new TierChange { From = from, To = to }));
        }

        [Fact]
        public void UnchangedTierHasNoAnnouncement()
        {
            Assert.Null(Messages.TierChanged(new TierChange { From = Tier.Alert, To = Tier.Alert }));
        }

        [Fact]
        public void StatusLine()
        {
            var t = new Territory
            {
                Rebel = new SiteCounts { SmallAirbases = 1, Factories = 2 },
                Occupier = new SiteCounts { LargeAirbases = 5, Depots = 12 },
            };
            string text = Messages.Status(42.4, Tier.Alert, t, 123.6f, 3 * 3600 + 7 * 60 + 5);
            Assert.Equal("Aggression 42 (ALERT) | Territory 3/20 sites (15%) | Rebel funds 124 | Campaign time 3h 07m", text);
        }

        [Fact]
        public void AirbaseCapturedNamesBase()
        {
            Assert.Equal("Rebels have captured Red Hill", Messages.AirbaseCaptured("Red Hill"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: build FAILS with `CS0103: The name 'Messages' does not exist`.

- [ ] **Step 3: Implement messages**

`src/Nuclearstasi.Core/Messages.cs`:
```csharp
using System;
using System.Globalization;

namespace Nuclearstasi.Core
{
    public static class Messages
    {
        public static string TierName(Tier tier)
        {
            switch (tier)
            {
                case Tier.Alert: return "ALERT";
                case Tier.Hostile: return "HOSTILE";
                case Tier.TotalWar: return "TOTAL WAR";
                default: return "QUIET";
            }
        }

        public static string TierChanged(TierChange change)
        {
            if (!change.Changed)
            {
                return null;
            }
            if (!change.Rose)
            {
                return "Occupation forces have stood down to " + TierName(change.To);
            }
            switch (change.To)
            {
                case Tier.Alert: return "Occupation forces are now on ALERT";
                case Tier.Hostile: return "Occupation forces are now HOSTILE";
                default: return "The occupation has declared TOTAL WAR";
            }
        }

        public static string Status(double aggression, Tier tier, Territory t, float rebelFunds, double elapsedSeconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds));
            return string.Format(
                CultureInfo.InvariantCulture,
                "Aggression {0:0} ({1}) | Territory {2}/{3} sites ({4:0}%) | Rebel funds {5:0} | Campaign time {6}h {7:00}m",
                aggression,
                TierName(tier),
                t.Rebel.Total,
                t.TotalSites,
                t.RebelShare * 100,
                rebelFunds,
                (int)time.TotalHours,
                time.Minutes);
        }

        public static string CampaignStarted()
        {
            return "Nuclearstasi: a new campaign has begun. The occupation does not know you exist... yet.";
        }

        public static string CampaignResumed(Tier tier)
        {
            return "Nuclearstasi: campaign resumed. Occupation status: " + TierName(tier);
        }

        public static string CorruptSave()
        {
            return "Nuclearstasi: the campaign save could not be read and was set aside. Starting a new campaign.";
        }

        public static string AirbaseCaptured(string name)
        {
            return "Rebels have captured " + name;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Passed!  - Failed: 0, Passed: 59`

- [ ] **Step 5: Commit**

```bash
git add src/Nuclearstasi.Core/Messages.cs tests/Nuclearstasi.Core.Tests/MessagesTests.cs
git commit -m "Add player-facing announcements and status line"
```

---

### Task 6: Plugin project, config binding and packaging

**Files:**
- Create: `src/Nuclearstasi.Plugin/Nuclearstasi.Plugin.csproj`, `src/Nuclearstasi.Plugin/Plugin.cs`, `src/Nuclearstasi.Plugin/PluginConfig.cs`, `scripts/package.sh`
- Modify: `Nuclearstasi.sln` (add project), `.gitignore` (add `dist/`)

**Interfaces:**
- Consumes: `Settings` and sub-settings (Task 1)
- Produces:
  - `Plugin.Guid = "com.nuclearstasi.mod"`, `Plugin.Log` (`ManualLogSource`)
  - `static Settings PluginConfig.Load(ConfigFile cfg)`
  - Later tasks add to `Plugin.Awake`: controller creation, patching, command registration (Task 8/9/10 show exact final file)

There is no unit test for this task (it only compiles against game DLLs). Verification is a successful build and the packaged output.

- [ ] **Step 1: Create the plugin project**

`src/Nuclearstasi.Plugin/Nuclearstasi.Plugin.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>Nuclearstasi</AssemblyName>
    <RootNamespace>Nuclearstasi.Plugin</RootNamespace>
    <Version>0.1.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Nuclearstasi.Core/Nuclearstasi.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Assembly-CSharp"><HintPath>$(GameLibDir)Assembly-CSharp.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Mirage"><HintPath>$(GameLibDir)Mirage.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UniTask"><HintPath>$(GameLibDir)UniTask.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine"><HintPath>$(GameLibDir)UnityEngine.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>$(GameLibDir)UnityEngine.CoreModule.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="BepInEx"><HintPath>$(GameLibDir)BepInEx.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="0Harmony"><HintPath>$(GameLibDir)0Harmony.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Bind config**

`src/Nuclearstasi.Plugin/PluginConfig.cs`:
```csharp
using BepInEx.Configuration;
using Nuclearstasi.Core;

namespace Nuclearstasi.Plugin
{
    internal static class PluginConfig
    {
        public static Settings Load(ConfigFile cfg)
        {
            var s = new Settings();

            EconomySettings e = s.Economy;
            e.BaseTrickle = cfg.Bind("Economy", "BaseTrickle", e.BaseTrickle, "Rebel income every 30 s regardless of territory").Value;
            e.SmallAirbase = cfg.Bind("Economy", "SmallAirbase", e.SmallAirbase, "Income every 30 s per small airbase held").Value;
            e.LargeAirbase = cfg.Bind("Economy", "LargeAirbase", e.LargeAirbase, "Income every 30 s per large airbase held").Value;
            e.Factory = cfg.Bind("Economy", "Factory", e.Factory, "Income every 30 s per factory held").Value;
            e.Depot = cfg.Bind("Economy", "Depot", e.Depot, "Income every 30 s per vehicle depot held").Value;
            e.LargeAirbaseRunwayMetres = cfg.Bind("Economy", "LargeAirbaseRunwayMetres", e.LargeAirbaseRunwayMetres, "An airbase with a runway at least this long counts as large").Value;

            AggressionSettings a = s.Aggression;
            a.AircraftKilled = cfg.Bind("Aggression", "AircraftKilled", a.AircraftKilled, "Aggression per enemy aircraft killed").Value;
            a.GroundUnitKilled = cfg.Bind("Aggression", "GroundUnitKilled", a.GroundUnitKilled, "Aggression per enemy ground vehicle or ship killed").Value;
            a.BuildingDestroyed = cfg.Bind("Aggression", "BuildingDestroyed", a.BuildingDestroyed, "Aggression per enemy building destroyed").Value;
            a.FactoryOrDepotCaptured = cfg.Bind("Aggression", "FactoryOrDepotCaptured", a.FactoryOrDepotCaptured, "Extra aggression per factory or depot inside a captured airbase").Value;
            a.AirbaseCaptured = cfg.Bind("Aggression", "AirbaseCaptured", a.AirbaseCaptured, "Aggression per airbase captured").Value;
            a.ConvoyDestroyed = cfg.Bind("Aggression", "ConvoyDestroyed", a.ConvoyDestroyed, "Aggression per nuclear convoy destroyed").Value;
            a.CapitalRadiusMetres = cfg.Bind("Aggression", "CapitalRadiusMetres", a.CapitalRadiusMetres, "Actions within this distance of the capital are multiplied").Value;
            a.CapitalMultiplier = cfg.Bind("Aggression", "CapitalMultiplier", a.CapitalMultiplier, "Multiplier for actions near the capital").Value;
            a.DecayAmount = cfg.Bind("Aggression", "DecayAmount", a.DecayAmount, "Aggression lost per decay interval").Value;
            a.DecayIntervalSeconds = cfg.Bind("Aggression", "DecayIntervalSeconds", a.DecayIntervalSeconds, "Seconds between decay steps").Value;
            a.FloorPerRebelShare = cfg.Bind("Aggression", "FloorPerRebelShare", a.FloorPerRebelShare, "Minimum aggression = this x rebel share of all sites").Value;
            a.AlertAt = cfg.Bind("Aggression", "AlertAt", a.AlertAt, "Aggression at which ALERT begins").Value;
            a.HostileAt = cfg.Bind("Aggression", "HostileAt", a.HostileAt, "Aggression at which HOSTILE begins").Value;
            a.TotalWarAt = cfg.Bind("Aggression", "TotalWarAt", a.TotalWarAt, "Aggression at which TOTAL WAR begins").Value;

            ReinforcementSettings r = s.Reinforcements;
            r.IntervalSeconds = cfg.Bind("Reinforcements", "IntervalSeconds", r.IntervalSeconds, "Seconds between occupier reinforcement waves").Value;
            r.WaveFraction = cfg.Bind("Reinforcements", "WaveFraction", r.WaveFraction, "Fraction of the occupier's starting supply sent per wave at full territory").Value;
            r.ShareExponent = cfg.Bind("Reinforcements", "ShareExponent", r.ShareExponent, "Wave size = WaveFraction x occupierShare^ShareExponent").Value;

            CampaignSettings c = s.Campaign;
            c.MissionPrefix = cfg.Bind("Campaign", "MissionPrefix", c.MissionPrefix, "Missions whose name starts with this activate the mod").Value;
            c.CapitalPrefix = cfg.Bind("Campaign", "CapitalPrefix", c.CapitalPrefix, "Airbase name prefix marking the enemy capital").Value;
            c.NukeDepotPrefix = cfg.Bind("Campaign", "NukeDepotPrefix", c.NukeDepotPrefix, "Airbase name prefix marking the enemy nuclear depot").Value;
            c.AutosaveIntervalSeconds = cfg.Bind("Campaign", "AutosaveIntervalSeconds", c.AutosaveIntervalSeconds, "Seconds between autosaves").Value;
            c.ApplyDelaySeconds = cfg.Bind("Campaign", "ApplyDelaySeconds", c.ApplyDelaySeconds, "Seconds after mission start before the saved campaign is applied").Value;

            return s;
        }
    }
}
```

- [ ] **Step 3: Minimal entry point**

`src/Nuclearstasi.Plugin/Plugin.cs`:
```csharp
using BepInEx;
using BepInEx.Logging;
using Nuclearstasi.Core;

namespace Nuclearstasi.Plugin
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.nuclearstasi.mod";
        public const string Name = "Nuclearstasi";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Settings settings = PluginConfig.Load(Config);
            Log.LogInfo($"{Name} {Version} loaded (mission prefix '{settings.Campaign.MissionPrefix}')");
        }
    }
}
```

- [ ] **Step 4: Packaging script**

`scripts/package.sh`:
```bash
#!/usr/bin/env bash
# Builds the plugin in Release and assembles dist/Nuclearstasi/ ready to copy into BepInEx/plugins/.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build src/Nuclearstasi.Plugin/Nuclearstasi.Plugin.csproj -c Release
out=src/Nuclearstasi.Plugin/bin/Release/net472
rm -rf dist && mkdir -p dist/Nuclearstasi
cp "$out/Nuclearstasi.dll" "$out/Nuclearstasi.Core.dll" dist/Nuclearstasi/
(cd dist && zip -qr Nuclearstasi.zip Nuclearstasi)
echo "Packaged dist/Nuclearstasi.zip"
```

Run:
```bash
chmod +x scripts/package.sh
printf 'dist/\n' >> .gitignore
dotnet sln add src/Nuclearstasi.Plugin/Nuclearstasi.Plugin.csproj
```

- [ ] **Step 5: Verify build and package**

Run: `./scripts/package.sh && unzip -l dist/Nuclearstasi.zip`
Expected: `Build succeeded.` and the zip lists exactly `Nuclearstasi/Nuclearstasi.dll` and `Nuclearstasi/Nuclearstasi.Core.dll`.

Run: `dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Passed!  - Failed: 0, Passed: 59`

- [ ] **Step 6: Commit**

```bash
git add src/Nuclearstasi.Plugin scripts/package.sh .gitignore Nuclearstasi.sln
git commit -m "Add BepInEx plugin project, config binding and packaging script"
```

---

### Task 7: Game queries, announcer and state sync

**Files:**
- Create: `src/Nuclearstasi.Plugin/Game/GameQueries.cs`, `src/Nuclearstasi.Plugin/Game/Announcer.cs`, `src/Nuclearstasi.Plugin/Game/StateSync.cs`

**Interfaces:**
- Consumes: `SiteCounts`, `Territory` (Task 1), `CampaignState` (Task 4), `Plugin.Log` (Task 6)
- Produces:
  - `static bool GameQueries.IsServer()`
  - `static IEnumerable<KeyValuePair<string, Airbase>> GameQueries.Airbases()` — non-null, non-attached (excludes carriers)
  - `static bool GameQueries.TryGetFactions(out FactionHQ rebels, out FactionHQ occupiers)` — rebels = joinable HQ, occupiers = `preventJoin` HQ
  - `static bool GameQueries.IsLarge(Airbase, float largeRunwayMetres)`
  - `static void GameQueries.CountBuildings(Airbase, out int factories, out int depots)`
  - `static Territory GameQueries.MeasureTerritory(FactionHQ rebels, FactionHQ occupiers, float largeRunwayMetres)`
  - `static string GameQueries.DisplayName(Airbase)`
  - `static Airbase GameQueries.FindByPrefix(string prefix)`
  - `static Dictionary<string,int> GameQueries.MissionSupplies(Mission, FactionHQ)`
  - `static void Announcer.All(string message)`
  - `static void StateSync.Capture(CampaignState, FactionHQ rebels, FactionHQ occupiers)`
  - `static void StateSync.Apply(CampaignState, FactionHQ rebels, FactionHQ occupiers)`
  - `static Dictionary<string,int> StateSync.ReadSupplies(FactionHQ)`

Game API facts these rely on (from the decompiled game): `FactionRegistry.airbaseLookup`, `FactionRegistry.GetAllHQs()`, `FactionRegistry.HqFromName(string)`, `FactionHQ.preventJoin`, `FactionHQ.faction.factionName`, `FactionHQ.factionFunds`, `FactionHQ.SetFunds`, `FactionHQ.AddSupplyUnit` (server-only), `FactionHQ.AircraftSupply`/`VehicleSupply` (`SyncDictionary<Definition, RuntimeSupply>` with `.Count`), `FactionHQ.GetPlayers(bool)`, `Player.Allocation`, `Airbase.CurrentHQ`, `Airbase.capture.ForceCapture(FactionHQ)`, `ICapturable.OnCapture(FactionHQ)`, `Airbase.runways[].Length`, `Airbase.buildings`, `Airbase.AttachedAirbase`, `Airbase.disabled`, `Airbase.center`, `Airbase.SavedAirbase.DisplayName`, `Airbase.HasStorage/GetWarheads/AddWarheads/RemoveWarheads`, `Encyclopedia.Lookup` (jsonKey → UnitDefinition), `Mission.GetFactionFromHq(hq, out MissionFaction)`, `MissionFaction.supplies` (`UnitType`, `Count`), `NetworkSceneSingleton<MessageManager>.i.RpcAllHQMessage(string)`.

No unit test (game types). Verification is a clean build; behaviour is verified in-game in Task 10.

- [ ] **Step 1: GameQueries**

`src/Nuclearstasi.Plugin/Game/GameQueries.cs`:
```csharp
using System;
using System.Collections.Generic;
using Mirage;
using Nuclearstasi.Core;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Nuclearstasi.Plugin.Game
{
    internal static class GameQueries
    {
        public static bool IsServer()
        {
            try
            {
                NetworkManager manager = NetworkManagerNuclearOption.i;
                return manager != null && manager.Server != null && manager.Server.Active;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static IEnumerable<KeyValuePair<string, Airbase>> Airbases()
        {
            foreach (KeyValuePair<string, Airbase> entry in FactionRegistry.airbaseLookup)
            {
                Airbase airbase = entry.Value;
                if (airbase == null || airbase.AttachedAirbase)
                {
                    continue;
                }
                yield return entry;
            }
        }

        public static bool TryGetFactions(out FactionHQ rebels, out FactionHQ occupiers)
        {
            rebels = null;
            occupiers = null;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null)
                {
                    continue;
                }
                if (hq.preventJoin)
                {
                    occupiers = hq;
                }
                else
                {
                    rebels = hq;
                }
            }
            return rebels != null && occupiers != null && rebels != occupiers;
        }

        public static bool IsLarge(Airbase airbase, float largeRunwayMetres)
        {
            if (airbase.runways == null)
            {
                return false;
            }
            foreach (Airbase.Runway runway in airbase.runways)
            {
                if (runway != null && runway.Length >= largeRunwayMetres)
                {
                    return true;
                }
            }
            return false;
        }

        public static void CountBuildings(Airbase airbase, out int factories, out int depots)
        {
            factories = 0;
            depots = 0;
            foreach (Building building in airbase.buildings)
            {
                if (building == null)
                {
                    continue;
                }
                if (building is VehicleDepot)
                {
                    depots++;
                }
                if (building.GetComponentInChildren<Factory>() != null)
                {
                    factories++;
                }
            }
        }

        public static Territory MeasureTerritory(FactionHQ rebels, FactionHQ occupiers, float largeRunwayMetres)
        {
            var territory = new Territory();
            foreach (KeyValuePair<string, Airbase> entry in Airbases())
            {
                Airbase airbase = entry.Value;
                if (airbase.disabled)
                {
                    continue;
                }
                bool large = IsLarge(airbase, largeRunwayMetres);
                CountBuildings(airbase, out int factories, out int depots);
                var counts = new SiteCounts
                {
                    SmallAirbases = large ? 0 : 1,
                    LargeAirbases = large ? 1 : 0,
                    Factories = factories,
                    Depots = depots,
                };
                if (airbase.CurrentHQ == rebels)
                {
                    territory.Rebel = territory.Rebel.Plus(counts);
                }
                else if (airbase.CurrentHQ == occupiers)
                {
                    territory.Occupier = territory.Occupier.Plus(counts);
                }
                else
                {
                    territory.Neutral = territory.Neutral.Plus(counts);
                }
            }
            return territory;
        }

        public static string DisplayName(Airbase airbase)
        {
            SavedAirbase saved = airbase.SavedAirbase;
            if (saved != null && !string.IsNullOrEmpty(saved.DisplayName))
            {
                return saved.DisplayName;
            }
            return airbase.NetworknetworkUniqueName;
        }

        public static Airbase FindByPrefix(string prefix)
        {
            foreach (KeyValuePair<string, Airbase> entry in Airbases())
            {
                string name = DisplayName(entry.Value) ?? "";
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }
            return null;
        }

        public static Dictionary<string, int> MissionSupplies(Mission mission, FactionHQ hq)
        {
            var supplies = new Dictionary<string, int>();
            mission.GetFactionFromHq(hq, out MissionFaction missionFaction);
            if (missionFaction?.supplies == null)
            {
                return supplies;
            }
            foreach (UnitCount count in missionFaction.supplies)
            {
                if (count == null || string.IsNullOrEmpty(count.UnitType))
                {
                    continue;
                }
                supplies.TryGetValue(count.UnitType, out int existing);
                supplies[count.UnitType] = existing + count.Count;
            }
            return supplies;
        }
    }
}
```

- [ ] **Step 2: Announcer**

`src/Nuclearstasi.Plugin/Game/Announcer.cs`:
```csharp
namespace Nuclearstasi.Plugin.Game
{
    internal static class Announcer
    {
        public static void All(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            Plugin.Log.LogInfo("[Announce] " + message);
            MessageManager manager = NetworkSceneSingleton<MessageManager>.i;
            if (manager == null)
            {
                Plugin.Log.LogWarning("MessageManager not available; announcement not sent");
                return;
            }
            manager.RpcAllHQMessage(message);
        }
    }
}
```

- [ ] **Step 3: StateSync**

`src/Nuclearstasi.Plugin/Game/StateSync.cs`:
```csharp
using System.Collections.Generic;
using Nuclearstasi.Core;
using NuclearOption.Networking;

namespace Nuclearstasi.Plugin.Game
{
    internal static class StateSync
    {
        public static void Capture(CampaignState state, FactionHQ rebels, FactionHQ occupiers)
        {
            state.AirbaseOwners.Clear();
            state.Warheads.Clear();
            state.Supplies.Clear();
            foreach (KeyValuePair<string, Airbase> entry in GameQueries.Airbases())
            {
                Airbase airbase = entry.Value;
                state.AirbaseOwners[entry.Key] = airbase.CurrentHQ != null ? airbase.CurrentHQ.faction.factionName : "";
                if (airbase.HasStorage())
                {
                    state.Warheads[entry.Key] = airbase.GetWarheads();
                }
            }
            state.Supplies[rebels.faction.factionName] = ReadSupplies(rebels);
            state.Supplies[occupiers.faction.factionName] = ReadSupplies(occupiers);

            float treasury = rebels.factionFunds;
            foreach (Player player in rebels.GetPlayers(false))
            {
                if (player != null)
                {
                    treasury += player.Allocation;
                }
            }
            state.RebelTreasury = treasury;
        }

        public static void Apply(CampaignState state, FactionHQ rebels, FactionHQ occupiers)
        {
            foreach (KeyValuePair<string, string> owner in state.AirbaseOwners)
            {
                if (!FactionRegistry.airbaseLookup.TryGetValue(owner.Key, out Airbase airbase) || airbase == null)
                {
                    Plugin.Log.LogWarning($"Saved airbase '{owner.Key}' not found in mission; skipped");
                    continue;
                }
                FactionHQ target = string.IsNullOrEmpty(owner.Value) ? null : FactionRegistry.HqFromName(owner.Value);
                if (airbase.CurrentHQ == target)
                {
                    continue;
                }
                if (target == null)
                {
                    ((ICapturable)airbase).OnCapture(null);
                }
                else
                {
                    airbase.capture.ForceCapture(target);
                }
                Plugin.Log.LogInfo($"Restored {GameQueries.DisplayName(airbase)} -> {(target == null ? "neutral" : owner.Value)}");
            }

            foreach (KeyValuePair<string, int> warheads in state.Warheads)
            {
                if (!FactionRegistry.airbaseLookup.TryGetValue(warheads.Key, out Airbase airbase) || airbase == null || !airbase.HasStorage())
                {
                    continue;
                }
                int diff = warheads.Value - airbase.GetWarheads();
                if (diff > 0)
                {
                    airbase.AddWarheads(diff);
                }
                else if (diff < 0)
                {
                    airbase.RemoveWarheads(-diff);
                }
            }

            ApplySupplies(state, rebels);
            ApplySupplies(state, occupiers);
            rebels.SetFunds(state.RebelTreasury);
        }

        public static Dictionary<string, int> ReadSupplies(FactionHQ hq)
        {
            var supplies = new Dictionary<string, int>();
            foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> entry in hq.AircraftSupply)
            {
                supplies[entry.Key.jsonKey] = entry.Value.Count;
            }
            foreach (KeyValuePair<VehicleDefinition, FactionHQ.RuntimeSupply> entry in hq.VehicleSupply)
            {
                supplies[entry.Key.jsonKey] = entry.Value.Count;
            }
            return supplies;
        }

        private static void ApplySupplies(CampaignState state, FactionHQ hq)
        {
            if (!state.Supplies.TryGetValue(hq.faction.factionName, out Dictionary<string, int> saved))
            {
                return;
            }
            Dictionary<string, int> current = ReadSupplies(hq);
            var keys = new HashSet<string>(saved.Keys);
            keys.UnionWith(current.Keys);
            foreach (string key in keys)
            {
                saved.TryGetValue(key, out int wanted);
                current.TryGetValue(key, out int have);
                if (wanted == have)
                {
                    continue;
                }
                if (!Encyclopedia.Lookup.TryGetValue(key, out UnitDefinition definition))
                {
                    Plugin.Log.LogWarning($"Unknown unit '{key}' in saved supplies; skipped");
                    continue;
                }
                hq.AddSupplyUnit(definition, wanted - have);
            }
        }
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/Nuclearstasi.Plugin/Nuclearstasi.Plugin.csproj`
Expected: `Build succeeded.` If a member is reported missing or inaccessible (e.g. `Player` namespace, `Airbase.Runway` nesting, `RuntimeSupply` nesting), look it up in the decompiled source (`ilspycmd -p -o <dir> lib/Assembly-CSharp.dll`, then grep) and adjust the reference — do not change behaviour.

- [ ] **Step 5: Commit**

```bash
git add src/Nuclearstasi.Plugin/Game
git commit -m "Add game queries, HQ announcer and campaign state sync"
```

---

### Task 8: Campaign controller and mission lifecycle

**Files:**
- Create: `src/Nuclearstasi.Plugin/Campaign/CampaignController.cs`, `src/Nuclearstasi.Plugin/Patches/MissionPatches.cs`
- Modify: `src/Nuclearstasi.Plugin/Plugin.cs` (full replacement shown)

**Interfaces:**
- Consumes: everything from Tasks 1–7
- Produces (`CampaignController`, used by patches and commands):
  - `static CampaignController Instance`
  - `bool Active`, `CampaignState State`, `AggressionMeter Aggression`, `FactionHQ Rebels`, `FactionHQ Occupiers`, `Territory Territory`
  - `void OnMissionStarted(Mission mission)`, `void OnMissionEnding()`, `void OnFactionDestroyed(FactionHQ hq)`
  - `void Tick(float deltaSeconds)`
  - `void SaveNow()`, `void ResetCampaign()`, `void SetAggression(double value)`, `string StatusText()`
  - Hooks filled in Task 9: `void BeforeDistributeFunds(FactionHQ hq)`, `void OnKillReported(FactionHQ reporter, Unit target)`

- [ ] **Step 1: Controller**

`src/Nuclearstasi.Plugin/Campaign/CampaignController.cs`:
```csharp
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Nuclearstasi.Core;
using Nuclearstasi.Plugin.Game;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Nuclearstasi.Plugin.Campaign
{
    internal sealed class CampaignController
    {
        private const double TerritoryRefreshSeconds = 5;
        private const double AggressionTickSeconds = 10;

        public static CampaignController Instance;

        private readonly Settings settings;
        private readonly CampaignStore store;
        private readonly ManualLogSource log;

        private ReinforcementPlanner reinforcements;
        private Mission pendingMission;
        private double pendingDelay = -1;
        private double territoryTimer;
        private double aggressionTimer;
        private double reinforcementTimer;
        private double autosaveTimer;
        private bool applying;

        public CampaignController(Settings settings, CampaignStore store, ManualLogSource log)
        {
            this.settings = settings;
            this.store = store;
            this.log = log;
        }

        public bool Active { get; private set; }
        public CampaignState State { get; private set; }
        public AggressionMeter Aggression { get; private set; }
        public FactionHQ Rebels { get; private set; }
        public FactionHQ Occupiers { get; private set; }
        public Airbase Capital { get; private set; }
        public Territory Territory { get; private set; } = new Territory();

        public void OnMissionStarted(Mission mission)
        {
            Deactivate();
            if (mission == null || mission.Name == null
                || !mission.Name.StartsWith(settings.Campaign.MissionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!GameQueries.IsServer())
            {
                log.LogInfo("Nuclearstasi mission detected but this machine is not the host; staying inactive");
                return;
            }
            pendingMission = mission;
            pendingDelay = settings.Campaign.ApplyDelaySeconds;
            log.LogInfo($"Nuclearstasi mission '{mission.Name}' detected; activating in {pendingDelay:0}s");
        }

        public void OnMissionEnding()
        {
            if (Active)
            {
                SaveNow();
            }
            Deactivate();
        }

        public void OnFactionDestroyed(FactionHQ hq)
        {
            if (Active && (hq == Rebels || hq == Occupiers))
            {
                SaveNow();
                Deactivate();
            }
        }

        public void Tick(float deltaSeconds)
        {
            if (pendingDelay >= 0)
            {
                pendingDelay -= deltaSeconds;
                if (pendingDelay < 0)
                {
                    Mission mission = pendingMission;
                    pendingMission = null;
                    try
                    {
                        Activate(mission);
                    }
                    catch (Exception e)
                    {
                        log.LogError("Activation failed: " + e);
                        Deactivate();
                    }
                }
                return;
            }
            if (!Active)
            {
                return;
            }
            if (Rebels == null || Occupiers == null)
            {
                Deactivate();
                return;
            }
            try
            {
                Step(deltaSeconds);
            }
            catch (Exception e)
            {
                log.LogError("Campaign tick failed: " + e);
            }
        }

        public void SaveNow()
        {
            if (!Active)
            {
                return;
            }
            try
            {
                StateSync.Capture(State, Rebels, Occupiers);
                State.Aggression = Aggression.Value;
                store.Save(State);
                log.LogInfo($"Campaign saved to {store.SavePath}");
            }
            catch (Exception e)
            {
                log.LogError("Campaign save failed: " + e);
            }
        }

        public void ResetCampaign()
        {
            Deactivate();
            store.Delete();
            Announcer.All("Nuclearstasi: campaign save wiped. Restart the mission to begin a new campaign.");
        }

        public void SetAggression(double value)
        {
            if (!Active)
            {
                return;
            }
            Announce(Aggression.Set(value));
            State.Aggression = Aggression.Value;
        }

        public string StatusText()
        {
            float funds = Rebels != null ? Rebels.factionFunds : 0f;
            return Messages.Status(Aggression.Value, Aggression.Tier, Territory, funds, State.ElapsedSeconds);
        }

        // Filled in Task 9.
        public void BeforeDistributeFunds(FactionHQ hq)
        {
        }

        // Filled in Task 9.
        public void OnKillReported(FactionHQ reporter, Unit target)
        {
        }

        private void Activate(Mission mission)
        {
            if (!GameQueries.TryGetFactions(out FactionHQ rebels, out FactionHQ occupiers))
            {
                log.LogError("Nuclearstasi needs exactly one joinable faction (rebels) and one faction with 'prevent join' set (occupiers); staying inactive");
                return;
            }
            Rebels = rebels;
            Occupiers = occupiers;
            Capital = GameQueries.FindByPrefix(settings.Campaign.CapitalPrefix);
            if (Capital == null)
            {
                log.LogWarning($"No airbase named '{settings.Campaign.CapitalPrefix}…' found; capital bonuses disabled");
            }

            LoadResult load = store.Load();
            bool resumed = load.Outcome == LoadOutcome.Loaded;
            if (load.Outcome == LoadOutcome.Corrupt)
            {
                log.LogError("Campaign save unreadable: " + load.Error);
                Announcer.All(Messages.CorruptSave());
            }
            State = resumed ? load.State : new CampaignState { MissionName = mission.Name };
            Aggression = new AggressionMeter(settings.Aggression, State.Aggression);

            if (resumed)
            {
                applying = true;
                try
                {
                    StateSync.Apply(State, Rebels, Occupiers);
                }
                finally
                {
                    applying = false;
                }
            }

            Dictionary<string, int> baseline = GameQueries.MissionSupplies(mission, Occupiers);
            reinforcements = new ReinforcementPlanner(baseline, settings.Reinforcements);
            Rebels.onAirbaseAdded += OnRebelAirbaseAdded;

            territoryTimer = aggressionTimer = reinforcementTimer = autosaveTimer = 0;
            Active = true;
            RefreshTerritory();
            log.LogInfo($"Activated: rebels={Rebels.faction.factionName}, occupiers={Occupiers.faction.factionName}, "
                + $"sites={Territory.TotalSites}, rebelShare={Territory.RebelShare:0.00}, resumed={resumed}");
            Announcer.All(resumed ? Messages.CampaignResumed(Aggression.Tier) : Messages.CampaignStarted());
            SaveNow();
        }

        private void Deactivate()
        {
            if (Rebels != null)
            {
                Rebels.onAirbaseAdded -= OnRebelAirbaseAdded;
            }
            Active = false;
            pendingDelay = -1;
            pendingMission = null;
            Rebels = null;
            Occupiers = null;
            Capital = null;
        }

        private void Step(double dt)
        {
            State.ElapsedSeconds += dt;

            territoryTimer += dt;
            if (territoryTimer >= TerritoryRefreshSeconds)
            {
                territoryTimer = 0;
                RefreshTerritory();
            }

            aggressionTimer += dt;
            if (aggressionTimer >= AggressionTickSeconds)
            {
                Announce(Aggression.Tick(aggressionTimer, Territory.RebelShare));
                aggressionTimer = 0;
                State.Aggression = Aggression.Value;
            }

            reinforcementTimer += dt;
            if (reinforcementTimer >= settings.Reinforcements.IntervalSeconds)
            {
                reinforcementTimer = 0;
                SendReinforcements();
            }

            autosaveTimer += dt;
            if (autosaveTimer >= settings.Campaign.AutosaveIntervalSeconds)
            {
                autosaveTimer = 0;
                SaveNow();
            }
        }

        private void RefreshTerritory()
        {
            Territory = GameQueries.MeasureTerritory(Rebels, Occupiers, settings.Economy.LargeAirbaseRunwayMetres);
        }

        // Filled in Task 9.
        private void SendReinforcements()
        {
        }

        // Filled in Task 9.
        private void OnRebelAirbaseAdded(Airbase airbase)
        {
        }

        private void Announce(TierChange change)
        {
            string message = Messages.TierChanged(change);
            if (message != null)
            {
                Announcer.All(message);
            }
        }
    }
}
```

- [ ] **Step 2: Mission lifecycle patches**

`src/Nuclearstasi.Plugin/Patches/MissionPatches.cs`:
```csharp
using HarmonyLib;
using Nuclearstasi.Plugin.Campaign;

namespace Nuclearstasi.Plugin.Patches
{
    [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.StartMission))]
    internal static class StartMissionPatch
    {
        private static void Postfix()
        {
            CampaignController.Instance?.OnMissionStarted(MissionManager.CurrentMission);
        }
    }

    [HarmonyPatch(typeof(MissionManager), "UnloadMission")]
    internal static class UnloadMissionPatch
    {
        private static void Prefix()
        {
            CampaignController.Instance?.OnMissionEnding();
        }
    }

    [HarmonyPatch(typeof(FactionHQ), "OnDestroy")]
    internal static class FactionHQDestroyPatch
    {
        private static void Prefix(FactionHQ __instance)
        {
            CampaignController.Instance?.OnFactionDestroyed(__instance);
        }
    }
}
```

- [ ] **Step 3: Wire up the plugin** (full replacement of `Plugin.cs`)

```csharp
using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Nuclearstasi.Core;
using Nuclearstasi.Plugin.Campaign;
using UnityEngine;

namespace Nuclearstasi.Plugin
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.nuclearstasi.mod";
        public const string Name = "Nuclearstasi";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            Settings settings = PluginConfig.Load(Config);
            string saveDir = Path.Combine(Paths.ConfigPath, "Nuclearstasi");
            CampaignController.Instance = new CampaignController(settings, new CampaignStore(saveDir), Logger);
            ApplyPatches();
            Log.LogInfo($"{Name} {Version} loaded (mission prefix '{settings.Campaign.MissionPrefix}', saves in {saveDir})");
        }

        private void Update()
        {
            CampaignController.Instance?.Tick(Time.deltaTime);
        }

        private void ApplyPatches()
        {
            harmony = new Harmony(Guid);
            foreach (Type type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    Log.LogInfo("Patched " + type.Name);
                }
                catch (Exception e)
                {
                    Log.LogError($"Patch {type.Name} failed (game update?): {e.Message}");
                }
            }
        }
    }
}
```

- [ ] **Step 4: Verify build and tests**

Run: `dotnet build src/Nuclearstasi.Plugin/Nuclearstasi.Plugin.csproj && dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Build succeeded.` and `Passed!  - Failed: 0, Passed: 59`

- [ ] **Step 5: Commit**

```bash
git add src/Nuclearstasi.Plugin
git commit -m "Add campaign controller with activation, autosave and mission lifecycle hooks"
```

---

### Task 9: Economy, reinforcement, aggression and escalation hooks

**Files:**
- Create: `src/Nuclearstasi.Plugin/Patches/FactionHQPatches.cs`
- Modify: `src/Nuclearstasi.Plugin/Campaign/CampaignController.cs` (replace the four "Filled in Task 9" methods)

**Interfaces:**
- Consumes: `CampaignController` members (Task 8), `Economy` (Task 1), `AggressionEvent` (Task 2), `Messages.AirbaseCaptured` (Task 5), `GameQueries.CountBuildings/DisplayName` (Task 7)
- Produces: patched `FactionHQ.DistributeFunds` (prefix), `FactionHQ.ReportKillAction` (postfix), `FactionHQ.CheckEscalation` (prefix, skipped while active)

- [ ] **Step 1: Patches**

`src/Nuclearstasi.Plugin/Patches/FactionHQPatches.cs`:
```csharp
using HarmonyLib;
using Nuclearstasi.Plugin.Campaign;

namespace Nuclearstasi.Plugin.Patches
{
    [HarmonyPatch(typeof(FactionHQ), "DistributeFunds")]
    internal static class DistributeFundsPatch
    {
        private static void Prefix(FactionHQ __instance)
        {
            CampaignController.Instance?.BeforeDistributeFunds(__instance);
        }
    }

    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.ReportKillAction))]
    internal static class ReportKillActionPatch
    {
        private static void Postfix(FactionHQ __instance, Unit target)
        {
            CampaignController.Instance?.OnKillReported(__instance, target);
        }
    }

    /// <summary>Vanilla nuclear escalation is disabled while a campaign is active.</summary>
    [HarmonyPatch(typeof(FactionHQ), "CheckEscalation")]
    internal static class CheckEscalationPatch
    {
        private static bool Prefix()
        {
            CampaignController controller = CampaignController.Instance;
            return controller == null || !controller.Active;
        }
    }
}
```

- [ ] **Step 2: Replace `BeforeDistributeFunds` and `OnKillReported` in `CampaignController.cs`**

```csharp
        public void BeforeDistributeFunds(FactionHQ hq)
        {
            if (!Active || hq != Rebels)
            {
                return;
            }
            float income = Economy.RebelIncome(Territory.Rebel, settings.Economy);
            hq.AddFunds(income);
            hq.regularIncome = Economy.RegularIncomeForPayout(income, hq.factionPlayers.Count);
        }

        public void OnKillReported(FactionHQ reporter, Unit target)
        {
            if (!Active || applying || reporter != Rebels || target == null || target.NetworkHQ != Occupiers)
            {
                return;
            }
            AggressionEvent e;
            if (target is Aircraft)
            {
                e = AggressionEvent.AircraftKilled;
            }
            else if (target is Building)
            {
                e = AggressionEvent.BuildingDestroyed;
            }
            else if (target is GroundVehicle || target is Ship)
            {
                e = AggressionEvent.GroundUnitKilled;
            }
            else
            {
                return;
            }
            Raise(e, NearCapital(target.transform.position));
        }
```

- [ ] **Step 3: Replace `SendReinforcements` and `OnRebelAirbaseAdded`, and add `Raise`/`NearCapital` in `CampaignController.cs`**

```csharp
        private void SendReinforcements()
        {
            Dictionary<string, int> wave = reinforcements.NextWave(Territory.OccupierShare);
            int total = 0;
            foreach (KeyValuePair<string, int> entry in wave)
            {
                if (Encyclopedia.Lookup.TryGetValue(entry.Key, out UnitDefinition definition))
                {
                    Occupiers.AddSupplyUnit(definition, entry.Value);
                    total += entry.Value;
                }
            }
            log.LogInfo($"Occupier reinforcements: {total} units (occupier share {Territory.OccupierShare:0.00})");
        }

        private void OnRebelAirbaseAdded(Airbase airbase)
        {
            if (!Active || applying || airbase == null || airbase.AttachedAirbase)
            {
                return;
            }
            bool nearCapital = NearCapital(airbase.center.position);
            Raise(AggressionEvent.AirbaseCaptured, nearCapital);
            GameQueries.CountBuildings(airbase, out int factories, out int depots);
            for (int i = 0; i < factories + depots; i++)
            {
                Raise(AggressionEvent.FactoryOrDepotCaptured, nearCapital);
            }
            RefreshTerritory();
            Announcer.All(Messages.AirbaseCaptured(GameQueries.DisplayName(airbase)));
            SaveNow();
        }

        private void Raise(AggressionEvent e, bool nearCapital)
        {
            Announce(Aggression.Add(e, nearCapital));
            State.Aggression = Aggression.Value;
            log.LogInfo($"Aggression +{e}{(nearCapital ? " (near capital)" : "")} -> {Aggression.Value:0.0} {Messages.TierName(Aggression.Tier)}");
        }

        private bool NearCapital(Vector3 position)
        {
            return Capital != null
                && Vector3.Distance(position, Capital.center.position) <= settings.Aggression.CapitalRadiusMetres;
        }
```

- [ ] **Step 4: Verify build and tests**

Run: `dotnet build src/Nuclearstasi.Plugin/Nuclearstasi.Plugin.csproj && dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Build succeeded.` and `Passed!  - Failed: 0, Passed: 59`

- [ ] **Step 5: Commit**

```bash
git add src/Nuclearstasi.Plugin
git commit -m "Hook territory income, reinforcements, aggression events and disable vanilla escalation"
```

---

### Task 10: `/ns` command, docs and spec update

**Files:**
- Create: `src/Nuclearstasi.Plugin/Commands/NsCommand.cs`
- Modify: `src/Nuclearstasi.Plugin/Plugin.cs` (register command), `src/Nuclearstasi.Plugin/Campaign/CampaignController.cs` (re-register on activation), `README.md`, `docs/superpowers/specs/2026-11-15-nuclearstasi-design.md`

**Interfaces:**
- Consumes: `CampaignController.Active/SaveNow/ResetCampaign/SetAggression/StatusText` (Task 8)
- Produces: `static void NsCommand.Register()`; chat command `/ns [status|save|new|aggro] [value]`

- [ ] **Step 1: Command**

`src/Nuclearstasi.Plugin/Commands/NsCommand.cs`:
```csharp
using Nuclearstasi.Plugin.Campaign;
using NuclearOption.ModdingApi;

namespace Nuclearstasi.Plugin.Commands
{
    internal static class NsCommand
    {
        private static readonly string[] Actions = { "status", "save", "new", "aggro" };

        public static void Register()
        {
            bool registered = ChatCommands.RegisterServerCommand(
                "ns",
                "Nuclearstasi campaign: status | save | new | aggro <0-100>",
                Handle,
                new[]
                {
                    ChatCommandArgument.FromChoices("action", Actions, optional: true),
                    new ChatCommandArgument("value", ChatArgumentType.Int, optional: true),
                });
            Plugin.Log.LogInfo(registered ? "Registered /ns command" : "/ns command already registered");
        }

        private static ChatCommandResult Handle(ChatCommandRequest request)
        {
            CampaignController controller = CampaignController.Instance;
            string action = request.Args.Count > 0 ? request.Args[0].ToLowerInvariant() : "status";

            if (action == "new")
            {
                if (!request.Sender.IsHost)
                {
                    return ChatCommandResult.Error(0, "Only the host can use /ns new");
                }
                controller?.ResetCampaign();
                return ChatCommandResult.Ok("Campaign save wiped. Restart the mission to begin a new campaign.");
            }

            if (controller == null || !controller.Active)
            {
                return ChatCommandResult.Ok("Nuclearstasi is not active in this mission.");
            }

            switch (action)
            {
                case "status":
                    return ChatCommandResult.Ok(controller.StatusText());
                case "save":
                    if (!request.Sender.IsHost) return ChatCommandResult.Error(0, "Only the host can use /ns save");
                    controller.SaveNow();
                    return ChatCommandResult.Ok("Campaign saved.");
                case "aggro":
                    if (!request.Sender.IsHost) return ChatCommandResult.Error(0, "Only the host can use /ns aggro");
                    if (!request.TryGetInt(1, out int value, out string error))
                    {
                        return ChatCommandResult.Error(0, error ?? "Usage: /ns aggro <0-100>");
                    }
                    controller.SetAggression(value);
                    return ChatCommandResult.Ok("Aggression set. " + controller.StatusText());
                default:
                    return ChatCommandResult.Error(0, "Usage: /ns [status|save|new|aggro <0-100>]");
            }
        }
    }
}
```

- [ ] **Step 2: Register it**

In `Plugin.cs` `Awake()`, after `ApplyPatches();` add:
```csharp
            Commands.NsCommand.Register();
```

In `CampaignController.Activate(...)`, immediately before `Active = true;` add (re-registration is harmless if already registered, and covers the game clearing mod commands between sessions):
```csharp
            Commands.NsCommand.Register();
```

- [ ] **Step 3: Verify build, tests and package**

Run: `./scripts/package.sh && dotnet test tests/Nuclearstasi.Core.Tests`
Expected: `Packaged dist/Nuclearstasi.zip` and `Passed!  - Failed: 0, Passed: 59`

- [ ] **Step 4: README with install, mission setup and in-game test checklist**

Replace `README.md` with:
````markdown
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
10. Nuclear escalation messages never appear.
11. A friend without the mod can join and sees the HQ messages.

## Building

Needs .NET SDK 8. Put the game/BepInEx DLLs in `lib/` (git-ignored, never committed): `Assembly-CSharp.dll`, `Mirage.dll`, `UniTask.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll` (from `NuclearOption_Data/Managed`), `BepInEx.dll`, `0Harmony.dll` (from `BepInEx/core`).

```bash
dotnet test tests/Nuclearstasi.Core.Tests
./scripts/package.sh
```
````

- [ ] **Step 5: Fold design adjustments into the spec**

In `docs/superpowers/specs/2026-11-15-nuclearstasi-design.md`:

Replace in section 5 the bullet starting `- Saved: ownership of every airbase/factory/depot;` with:
```markdown
- Saved: ownership of every airbase (factories and depots inside an airbase
  change hands with it); aggression; warheads per base; the **rebel
  treasury** (rebel faction funds plus every connected rebel player's funds —
  vanilla returns a leaving player's funds to the faction pool, so funds are
  kept as one pool rather than per Steam ID); unit supply stockpiles; capital
  unlocked flag; elapsed campaign time; campaign status (active / won / lost);
  save format version. On load the treasury goes back into the rebel pool and
  reaches players through vanilla's join allowance and excess-funds payout.
```

Add to the end of section 4.3:
```markdown
- v1 counts kills reported to the rebel HQ by players
  (`FactionHQ.ReportKillAction`); kills by rebel AI do not raise aggression.
- "Capture factory / depot" applies per factory or depot inside a captured
  airbase, on top of the airbase's own +15.
```

Add to the end of section 4.5:
```markdown
- Until nuke convoys are implemented (Plan 3), nuclear weapons are fully
  disabled in this mode.
```

- [ ] **Step 6: Commit**

```bash
git add src README.md docs/superpowers/specs/2026-11-15-nuclearstasi-design.md
git commit -m "Add /ns command, README with install and test checklist, spec adjustments"
```

---

## Self-review notes

- Spec coverage for this plan: activation (T8), economy (T1, T9), reinforcements (T3, T9), aggression incl. capital ×2, decay, floor, tiers, announcements (T2, T5, T9), escalation disabled (T9), persistence incl. corrupt handling and autosave/quit (T4, T7, T8), config (T6), commands `/ns status|new|aggro` + `save` (T10), testing (unit T1–T5, in-game checklist T10). Deferred by design to Plans 2–3: commander behaviour, production/garrisons, nuke convoys, capital lock/victory/defeat, `/ns convoy`, `/ns unlock`, shipped mission file.
- Known in-game risks to watch in the log: activation delay vs. airbase setup timing (`ApplyDelaySeconds`), whether `StartMission` fires on the host for every mission start, chat command registration lifetime, and large-airbase runway threshold (log lists `sites=`; tune `LargeAirbaseRunwayMetres`).
