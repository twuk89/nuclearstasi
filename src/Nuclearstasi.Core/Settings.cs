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
