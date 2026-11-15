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
