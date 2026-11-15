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
        private const int MaxConsecutiveFailures = 300;

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
        private int consecutiveFailures;
        private int expectedAirbaseCount;
#pragma warning disable CS0414 // read by the Task 9 airbase-added handler
        private bool applying;
#pragma warning restore CS0414

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
            if (Active)
            {
                OnMissionEnding();
            }
            else
            {
                Deactivate();
            }
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
                        if (GameQueries.IsServer() && MissionManager.CurrentMission == mission)
                        {
                            Activate(mission);
                        }
                        else
                        {
                            log.LogInfo("Nuclearstasi activation dropped: mission changed or this machine is no longer the host");
                        }
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
                consecutiveFailures = 0;
            }
            catch (Exception e)
            {
                consecutiveFailures++;
                if (consecutiveFailures == 1 || consecutiveFailures % MaxConsecutiveFailures == 0)
                {
                    log.LogError("Campaign tick failed: " + e);
                }
                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    log.LogError("Nuclearstasi disabled for this mission after repeated errors");
                    Deactivate();
                }
            }
        }

        public void SaveNow()
        {
            if (!Active)
            {
                return;
            }
            if (Rebels == null || Occupiers == null || CountAirbases() < expectedAirbaseCount)
            {
                log.LogWarning("Nuclearstasi: scene is unloading; keeping the last good save");
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
            if (resumed && !string.IsNullOrEmpty(load.State.MissionName)
                && !string.Equals(load.State.MissionName, mission.Name, StringComparison.OrdinalIgnoreCase))
            {
                log.LogWarning($"Saved campaign belongs to mission '{load.State.MissionName}', not '{mission.Name}'; staying inactive");
                Announcer.All($"Nuclearstasi: the saved campaign belongs to mission '{load.State.MissionName}'. Use /ns new to start a new campaign for this mission.");
                Deactivate();
                return;
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
            consecutiveFailures = 0;
            expectedAirbaseCount = CountAirbases();
            Active = true;
            RefreshTerritory();
            log.LogInfo($"Activated: rebels={Rebels.faction.factionName}, occupiers={Occupiers.faction.factionName}, "
                + $"sites={Territory.TotalSites}, rebelShare={Territory.RebelShare:0.00}, resumed={resumed}");
            Announcer.All(resumed ? Messages.CampaignResumed(Aggression.Tier) : Messages.CampaignStarted());
            SaveNow();
        }

        private static int CountAirbases()
        {
            int count = 0;
            foreach (var _ in GameQueries.Airbases())
            {
                count++;
            }
            return count;
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
